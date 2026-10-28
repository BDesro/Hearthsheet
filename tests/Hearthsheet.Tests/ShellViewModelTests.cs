using Hearthsheet.Core.Application;
using Hearthsheet.Core.Content;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;
using Hearthsheet.Core.Serialization;
using Hearthsheet.Core.Sync;
using Hearthsheet.Presentation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hearthsheet.Tests;

[Collection("CommandRequery")]
public sealed class ShellViewModelTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ManualTime _time = new();
    private readonly FakeSession _session = new();
    private readonly FakeCloud _cloud;
    private readonly Machine _other;
    private readonly Machine _here;
    private readonly FakeDialogs _dialogs = new();
    private readonly Queue<Action> _posted = new();
    private readonly CharacterLibrary _library;
    private readonly ShellViewModel _shell;

    public ShellViewModelTests()
    {
        _cloud = new FakeCloud(_time);
        _other = new Machine(_dir, "other", _cloud, _session, _time);
        _here = new Machine(_dir, "here", _cloud, _session, _time);
        _library = new CharacterLibrary(_here.Repo, new PortableCharacterFile(), "1.0.0", NullLogger<CharacterLibrary>.Instance);
        _shell = new ShellViewModel(_here.Repo, _library, RestService.CreateDefault(), _dialogs, NullLoggerFactory.Instance,
            _posted.Enqueue, _time, _here.Sync);
    }

    public void Dispose() => _dir.Dispose();

    /// <summary>A character made on the other device, synced here and open in the shell.</summary>
    private async Task<Guid> OpenSyncedCharacter()
    {
        var c = _other.Add("Arannis");
        await _other.SyncAsync();
        await _here.SyncAsync();
        _shell.Start();
        Assert.Equal(c.Id, _shell.OpenCharacterId);
        return c.Id;
    }

    private Task<SyncResult> SyncHere() => _here.Sync.RunAsync(() => _shell.OpenCharacterId, waitIfBusy: false, CancellationToken.None);

    [Fact]
    public async Task Delete_AsksFirst_AndKeepsTheCharacterWhenDeclined()
    {
        var c = _here.Add("Arannis");
        _shell.Start();
        _dialogs.ConfirmAnswer = false;

        await _shell.DeleteSelectedAsync();

        Assert.Single(_dialogs.Confirms);
        Assert.Single(_shell.Characters);
        Assert.Equal(c.Id, _shell.OpenCharacterId);
        Assert.NotNull(_here.Repo.Load(c.Id));
    }

    [Fact]
    public async Task ApplySyncResult_ReopensTheOpenCharacter_WithUpdatedStatus()
    {
        var id = await OpenSyncedCharacter();
        _other.Rename(id, "Arannis the Wise");
        await _other.SyncAsync();

        var result = await SyncHere();
        Assert.Single(result.Deferred);
        _shell.ApplySyncResult(result);

        Assert.Equal("Updated from another device", _shell.SaveStatus);
        Assert.Equal("Arannis the Wise", _shell.Current?.Character.Identity.Name);
        Assert.Single(_shell.Characters);
    }

    [Fact]
    public async Task ApplySyncResult_KeepsAConflictCopy_WhenTheOpenCharacterHasUnsavedEdits()
    {
        var id = await OpenSyncedCharacter();
        _shell.Current!.Character.Identity.Name = "Local edit";
        _other.Rename(id, "Arannis the Wise");
        await _other.SyncAsync();

        _shell.ApplySyncResult(await SyncHere());

        Assert.Equal("Updated from another device — your edits were kept as a conflict copy", _shell.SaveStatus);
        Assert.Equal("Arannis the Wise", _shell.Current?.Character.Identity.Name);
        Assert.Equal(2, _shell.Characters.Count);
        Assert.Contains(_shell.Characters, s => s.Id != id && s.Name.Contains("Local edit"));
    }

    [Fact]
    public async Task Import_InvalidText_ShowsErrorAndReturnsFalse()
    {
        var ok = await _shell.ImportAsync(() => _library.Import("not json", "x.dndchar"), "x.dndchar");

        Assert.False(ok);
        Assert.StartsWith("This file could not be imported:", Assert.Single(_dialogs.Errors));
        Assert.Empty(_shell.Characters);
    }

    [Fact]
    public void SyncStatus_Unsynced()
    {
        Assert.Equal("", SyncStatus.Unsynced(0));
        Assert.Equal("1 change not yet synced", SyncStatus.Unsynced(1));
        Assert.Equal("3 changes not yet synced", SyncStatus.Unsynced(3));
    }

    [Fact]
    public void SyncStatus_SignedOut() =>
        Assert.Equal("Signed out – sign in to resume sync", SyncStatus.For(SyncResult.Of(SyncOutcome.SignedOut), _time.GetLocalNow()));

    private sealed class FakeDialogs : IDialogs
    {
        public bool ConfirmAnswer { get; set; }
        public List<string> Confirms { get; } = [];
        public List<string> Errors { get; } = [];
        public Task<bool> ConfirmAsync(string message, string title = "Confirm")
        {
            Confirms.Add(message);
            return Task.FromResult(ConfirmAnswer);
        }
        public Task InfoAsync(string message) => Task.CompletedTask;
        public Task ErrorAsync(string message)
        {
            Errors.Add(message);
            return Task.CompletedTask;
        }
        public Task<NewCharacterOptions?> NewCharacterAsync() => Task.FromResult<NewCharacterOptions?>(null);
        public Task<(string ClassName, LevelUpOptions Options)?> LevelUpAsync(Character c, string? className = null) =>
            Task.FromResult<(string ClassName, LevelUpOptions Options)?>(null);
        public Task<QuickAddResult?> QuickAddAsync(string title, IReadOnlyList<object>? kinds, object? defaultKind, bool withDescription) =>
            Task.FromResult<QuickAddResult?>(null);
        public Task<bool> ShortRestAsync(PlayViewModel play, Character character) => Task.FromResult(false);
    }
}
