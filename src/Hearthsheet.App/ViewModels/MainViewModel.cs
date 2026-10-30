using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using Hearthsheet.App.Composition;
using Hearthsheet.Presentation;
using Hearthsheet.Core.Application;
using Hearthsheet.Core.Domain;
using Hearthsheet.Core.Rules;
using Hearthsheet.Infrastructure;
using Hearthsheet.Infrastructure.Updates;
using Microsoft.Extensions.Logging;

namespace Hearthsheet.App.ViewModels;

public sealed record MainServices(
    ICharacterRepository Repository, CharacterLibrary Library, RestService Rest, UpdateService Updates, AppSettings Settings, AppPaths Paths,
    Dialogs Dialogs, ILoggerFactory Loggers, CloudServices? Cloud);

/// <summary>
/// The desktop window: the shared <see cref="ShellViewModel"/> plus what only the desktop has (file dialogs and paths,
/// updates, zoom, logs and data folder, the selected view).
/// </summary>
public sealed class MainViewModel : Observable
{
    private readonly MainServices _s;
    private readonly ILogger _log;

    public MainViewModel(MainServices services)
    {
        _s = services;
        _log = services.Loggers.CreateLogger<MainViewModel>();
        // Constructed on the UI thread; the autosave timer and sheet refreshes post back to it.
        var dispatcher = Dispatcher.CurrentDispatcher;
        Shell = new ShellViewModel(services.Repository, services.Library, services.Rest, services.Dialogs, services.Loggers,
            action => dispatcher.BeginInvoke(DispatcherPriority.DataBind, action), TimeProvider.System, services.Cloud?.Sync)
        {
            BackupFolder = services.Paths.Backups,
        };
        Shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.Current)) Raise(nameof(HasCharacter));
        };
        Update = new UpdateViewModel(services.Updates, services.Paths, services.Dialogs, services.Settings.Updates, _log, BeforeUpdateShutdown);
        Sync = new SyncViewModel(services.Cloud, Shell, services.Dialogs, services.Loggers.CreateLogger<SyncViewModel>());

        NewCharacter = new RelayCommand(CreateCharacterAsync);
        DuplicateCharacter = new RelayCommand(Shell.Duplicate, () => Shell.Current is not null);
        DeleteCharacter = new RelayCommand(Shell.DeleteSelectedAsync, () => Shell.SelectedSummary is not null);
        ImportCharacter = new RelayCommand(ImportAsync);
        ExportCharacter = new RelayCommand(ExportAsync, () => Shell.Current is not null);
        ExportAllCharacters = new RelayCommand(ExportAllAsync);
        SaveCharacter = new RelayCommand(() => Shell.Save(), () => Shell.Current is not null);
        ShowView = new RelayCommand(p => SelectedView = int.TryParse(p as string, out var v) ? v : 0, _ => Shell.Current is not null);
        ZoomIn = new RelayCommand(() => Zoom += 0.1);
        ZoomOut = new RelayCommand(() => Zoom -= 0.1);
        ZoomReset = new RelayCommand(() => Zoom = 1.0);
        OpenLogs = new RelayCommand(() => Process.Start(new ProcessStartInfo(_s.Paths.Logs) { UseShellExecute = true }));
        OpenDataFolder = new RelayCommand(() => Process.Start(new ProcessStartInfo(_s.Paths.Root) { UseShellExecute = true }));
    }

    public ShellViewModel Shell { get; }
    public UpdateViewModel Update { get; }
    public SyncViewModel Sync { get; }

    public string AppVersion => AppSettings.AppVersion;

    public bool HasCharacter => Shell.Current is not null;

    /// <summary>0 = Sheet, 1 = Details, 2 = Play.</summary>
    public int SelectedView { get; set => Set(ref field, Math.Clamp(value, 0, 2)); }

    public double Zoom { get; set => Set(ref field, Math.Round(Math.Clamp(value, 0.5, 2.5), 2)); } = 1.0;

    public ICommand NewCharacter { get; }
    public ICommand DuplicateCharacter { get; }
    public ICommand DeleteCharacter { get; }
    public ICommand ImportCharacter { get; }
    public ICommand ExportCharacter { get; }
    public ICommand ExportAllCharacters { get; }
    public ICommand SaveCharacter { get; }
    public ICommand ShowView { get; }
    public ICommand ZoomIn { get; }
    public ICommand ZoomOut { get; }
    public ICommand ZoomReset { get; }
    public ICommand OpenLogs { get; }
    public ICommand OpenDataFolder { get; }

    public void OnStarted()
    {
        Shell.Start();
        if (_s.Settings.Updates.Enabled && _s.Settings.Updates.CheckOnStartup && _s.Settings.Updates.IsConfigured)
            _ = Update.CheckInBackgroundAsync();
        _ = Sync.StartAsync();
    }

    /// <summary>Called when the window is closing. Returns false to cancel the close.</summary>
    public async Task<bool> OnClosingAsync()
    {
        if (Shell.Current is null || !Shell.Current.Session.IsDirty) return true;
        if (Shell.Save()) return true;
        return await _s.Dialogs.ConfirmAsync("The character could not be saved. Close anyway and lose recent changes?", "Unsaved changes");
    }

    private async Task CreateCharacterAsync()
    {
        var before = Shell.Current;
        await Shell.CreateAsync();
        if (Shell.Current != before) SelectedView = 1;
    }

    private async Task ImportAsync()
    {
        var path = _s.Dialogs.OpenCharacterFile();
        if (path is null) return;
        await Shell.ImportAsync(() => _s.Library.Import(path), Path.GetFileName(path));
    }

    private async Task ExportAsync()
    {
        if (Shell.Current is not { } current) return;
        var path = _s.Dialogs.SaveCharacterFile(current.Title);
        if (path is null) return;
        try
        {
            _s.Library.Export(current.Character, path);
            current.Play.Record($"Exported to {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Export failed");
            await _s.Dialogs.ErrorAsync($"The character could not be exported:\n\n{ex.Message}");
        }
    }

    private async Task ExportAllAsync()
    {
        if (!Shell.Save()) return;
        var path = _s.Dialogs.SaveZipFile($"Hearthsheet characters {DateTime.Now:yyyy-MM-dd}");
        if (path is null) return;
        var temp = Path.GetFullPath(path) + ".tmp";
        IReadOnlyList<string> skipped;
        try
        {
            using (var stream = File.Create(temp)) skipped = _s.Library.ExportAll(stream);
            File.Move(temp, Path.GetFullPath(path), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Export all failed");
            try { File.Delete(temp); } catch (IOException) { }
            await _s.Dialogs.ErrorAsync($"The characters could not be exported:\n\n{ex.Message}");
            return;
        }
        if (skipped.Count > 0)
            await _s.Dialogs.ErrorAsync($"These characters could not be read and are not in the zip:\n\n{string.Join("\n", skipped)}");
    }

    /// <summary>Before the updater takes over: persist everything. Returns false to abort the update.</summary>
    private bool BeforeUpdateShutdown() => Shell.Save();
}
