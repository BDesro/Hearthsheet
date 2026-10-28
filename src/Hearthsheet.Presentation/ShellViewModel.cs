using System.Collections.ObjectModel;
using Hearthsheet.Core.Application;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;
using Hearthsheet.Core.Serialization;
using Hearthsheet.Core.Sync;
using Microsoft.Extensions.Logging;

namespace Hearthsheet.Presentation;

/// <summary>
/// The character list, the open character, saving/autosave, create/duplicate/delete/import and applying sync results.
/// Shared by desktop and web so their behaviour and status texts can't drift.
/// </summary>
public sealed class ShellViewModel : Observable
{
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromSeconds(2);

    private readonly ICharacterRepository _repository;
    private readonly CharacterLibrary _library;
    private readonly RestService _rest;
    private readonly IDialogs _dialogs;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger _log;
    private readonly Action<Action> _postToUi;
    private readonly TimeProvider _time;
    private readonly CloudSync? _sync;
    private readonly ITimer _autosave;

    public ShellViewModel(ICharacterRepository repository, CharacterLibrary library, RestService rest, IDialogs dialogs,
        ILoggerFactory loggers, Action<Action> postToUi, TimeProvider time, CloudSync? sync)
    {
        _repository = repository;
        _library = library;
        _rest = rest;
        _dialogs = dialogs;
        _loggers = loggers;
        _log = loggers.CreateLogger<ShellViewModel>();
        _postToUi = postToUi;
        _time = time;
        _sync = sync;
        // Fires off the UI thread on the desktop; the save itself runs on the UI thread. A tick that raced a stop
        // (a manual save, a sync reopening the character) finds nothing dirty and does nothing.
        _autosave = time.CreateTimer(_ => postToUi(() => { if (Current?.Session.IsDirty == true) Save(); }),
            null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Where database backups are kept, named in the "could not be opened" error when set (desktop).</summary>
    public string? BackupFolder { get; init; }

    public ObservableCollection<CharacterSummary> Characters { get; } = [];

    // Read by the background sync pass, so it is published through a volatile reference.
    private volatile Character? _openCharacter;
    public Guid? OpenCharacterId => _openCharacter?.Id;

    /// <summary>Setting it opens that character.</summary>
    public CharacterSummary? SelectedSummary
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            field = value;
            Raise();
            if (value is not null && value.Id != Current?.Character.Id) Open(value.Id);
        }
    }

    public CharacterViewModel? Current
    {
        get;
        private set
        {
            field?.Dispose();
            field = value;
            _openCharacter = value?.Character;
            Raise();
        }
    }

    public string SaveStatus { get; private set => Set(ref field, value); } = "";

    /// <summary>After every successful save.</summary>
    public event EventHandler? Saved;

    public void Start()
    {
        ReloadList();
        SelectedSummary = Characters.FirstOrDefault();
    }

    public void ReloadList(Guid? select = null)
    {
        try
        {
            var list = _repository.List();
            Characters.Clear();
            foreach (var summary in list) Characters.Add(summary);
            // Re-point the selection at the refreshed summary; the character itself is already open.
            if (select is { } id) SelectedSummary = Characters.FirstOrDefault(c => c.Id == id);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not list characters");
            _ = _dialogs.ErrorAsync("The character list could not be read. See the log for details.");
        }
    }

    private void Open(Guid id)
    {
        if (Current is not null && Current.Session.IsDirty && !Save()) return;
        try
        {
            var character = _repository.Load(id);
            if (character is null)
            {
                _ = _dialogs.ErrorAsync("That character no longer exists.");
                ReloadList();
                return;
            }
            SetCurrent(character);
        }
        catch (CharacterFormatException ex)
        {
            _log.LogError(ex, "Character {Id} could not be loaded", id);
            var backups = BackupFolder is null ? "" : $"\n\nA backup of the database is kept in:\n{BackupFolder}";
            _ = _dialogs.ErrorAsync($"This character could not be opened:\n\n{ex.Message}{backups}");
        }
    }

    private void SetCurrent(Character character)
    {
        var session = new CharacterSession(character);
        session.Changed += OnCharacterChanged;
        Current = new CharacterViewModel(session, _rest, _dialogs, _loggers.CreateLogger("Gameplay"), _postToUi);
        SaveStatus = "All changes saved";
        _log.LogDebug("View: character {Name} opened", character.Identity.Name);
    }

    private void OnCharacterChanged(object? sender, EventArgs e)
    {
        SaveStatus = "Unsaved changes";
        _autosave.Change(AutosaveDelay, Timeout.InfiniteTimeSpan);
    }

    private void StopAutosave() => _autosave.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    /// <returns>True when there was nothing to save or the save succeeded.</returns>
    public bool Save()
    {
        StopAutosave();
        if (Current is null) return true;
        try
        {
            _repository.Save(Current.Character);
        }
        catch (Exception ex)
        {
            // Each repository throws its own types (SQLite, IndexedDB, IO); none of them may lose the in-memory edits.
            _log.LogError(ex, "Save failed");
            SaveStatus = "Save failed — changes are still in memory";
            _ = _dialogs.ErrorAsync($"The character could not be saved:\n\n{ex.Message}");
            return false;
        }
        Current.Session.MarkSaved();
        SaveStatus = $"Saved {_time.GetLocalNow():HH:mm:ss}";
        RefreshSummary(Current.Character);
        Saved?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Applies a finished sync pass: remote changes to the open character (deferred by the pass so its
    /// autosave can't overwrite them), then the refreshed list.
    /// </summary>
    public void ApplySyncResult(SyncResult result)
    {
        string? status = null;
        foreach (var change in result.Deferred)
        {
            var open = Current?.Character.Id == change.Id ? Current : null;
            var copy = _sync!.ApplyDeferred(change, open?.Character, open?.Session.IsDirty == true);
            if (open is null) continue;

            StopAutosave();
            Current = null; // drop the stale in-memory model without saving it
            if (change.Remote is not null)
            {
                Open(change.Id);
                status = copy is null
                    ? "Updated from another device"
                    : "Updated from another device — your edits were kept as a conflict copy";
            }
            else if (copy is not null)
            {
                Open(copy.Id);
                status = "Deleted on another device — your edits were kept as a new character";
            }
            else
            {
                status = "This character was deleted on another device";
            }
        }
        if (!result.HasChanges) return;
        ReloadList(Current?.Character.Id);
        if (Current is null) SelectedSummary = Characters.FirstOrDefault();
        if (status is not null) SaveStatus = status;
    }

    /// <summary>Runs an operation that rewrites character ids with no character open. False if the open one couldn't be saved.</summary>
    public bool WithCharacterClosed(Action action)
    {
        if (!Save()) return false;
        Current = null;
        SelectedSummary = null;
        try
        {
            action();
        }
        finally
        {
            ReloadList();
            SelectedSummary = Characters.FirstOrDefault();
        }
        return true;
    }

    /// <summary>Keeps the sidebar entry (name, class, level) in step with edits without reloading everything.</summary>
    private void RefreshSummary(Character c)
    {
        var index = Characters.ToList().FindIndex(s => s.Id == c.Id);
        var fresh = _repository.List().FirstOrDefault(s => s.Id == c.Id);
        if (index < 0 || fresh is null || fresh == Characters[index]) return;
        var wasSelected = SelectedSummary?.Id == c.Id;
        Characters[index] = fresh;
        // Same character is already open; the SelectedSummary setter skips reopening when ids match.
        if (wasSelected) SelectedSummary = fresh;
    }

    public async Task CreateAsync()
    {
        var options = await _dialogs.NewCharacterAsync();
        if (options is null || !Save()) return;
        var character = _library.Create(options);
        SetCurrent(character);
        ReloadList(character.Id);
    }

    public void Duplicate()
    {
        if (Current is null || !Save()) return;
        var copy = _library.Duplicate(Current.Character);
        SetCurrent(copy);
        ReloadList(copy.Id);
    }

    public async Task DeleteSelectedAsync()
    {
        var target = SelectedSummary;
        if (target is null) return;
        if (!await _dialogs.ConfirmAsync($"Permanently delete “{target.Name}”?\n\nExport it first if you may want it back. This cannot be undone.", "Delete character"))
            return;
        if (Current?.Character.Id == target.Id)
        {
            StopAutosave();
            Current = null;
        }
        _repository.Delete(target.Id);
        SelectedSummary = null;
        ReloadList();
        SelectedSummary = Characters.FirstOrDefault();
        SaveStatus = "";
    }

    /// <param name="import">Reads, validates and saves the character (the desktop from a file, the web from text).</param>
    /// <returns>True when the character was imported and opened.</returns>
    public async Task<bool> ImportAsync(Func<Character> import, string sourceName)
    {
        try
        {
            if (!Save()) return false;
            var character = import();
            SetCurrent(character);
            ReloadList(character.Id);
            Current?.Play.Record($"Imported from {sourceName}.");
            return true;
        }
        catch (CharacterFormatException ex)
        {
            _log.LogWarning("Import rejected: {Reason}", ex.Message);
            await _dialogs.ErrorAsync($"This file could not be imported:\n\n{ex.Message}");
        }
        catch (IOException ex)
        {
            _log.LogWarning(ex, "Import failed");
            await _dialogs.ErrorAsync($"The file could not be read:\n\n{ex.Message}");
        }
        return false;
    }
}
