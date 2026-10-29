using Hearthsheet.Core.Storage;
using Hearthsheet.Core.Sync;
using Hearthsheet.Presentation;
using Microsoft.Extensions.Logging;

namespace Hearthsheet.Web;

/// <summary>
/// When the web runs sync passes (spec decision 10): after the shell opens, every 5 minutes while the tab is visible,
/// when the browser is back online, and when the tab is hidden. The browser has one thread, so a pass runs on it as
/// async code and saves interleave with it only at awaits. No Blazor here, so the tests link this file.
/// </summary>
public sealed class WebSync : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly CloudSync _sync;
    private readonly ShellViewModel _shell;
    private readonly ICloudSession _auth;
    private readonly OrderedJournal _journal;
    private readonly ILogger _log;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private bool _disposed;

    public WebSync(CloudSync sync, ShellViewModel shell, ICloudSession auth, OrderedJournal journal, ILogger log,
        Action<Action> postToUi, TimeProvider time)
    {
        (_sync, _shell, _auth, _journal, _log, _time) = (sync, shell, auth, journal, log, time);
        _timer = time.CreateTimer(_ => postToUi(Tick), null, Interval, Interval);
    }

    public bool IsBusy { get; private set; }
    public string Status { get; private set; } = "";

    /// <summary>Whether the tab is visible; the 5-minute timer only syncs then.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Raised after a pass (or a skipped one) has set <see cref="Status"/>.</summary>
    public event Action? Changed;

    public void Tick()
    {
        if (IsVisible) _ = RunAsync();
    }

    public void Online()
    {
        _log.LogInformation("Back online; syncing");
        _ = RunAsync();
    }

    /// <summary>The page was hidden and the caller has already saved: once that is written, sync. Not awaited by the caller.</summary>
    public async Task PageHiddenAsync()
    {
        await _journal.FlushAsync();
        await RunAsync();
    }

    public async Task RunAsync()
    {
        if (IsBusy || _disposed) return;
        if (_auth.UserId is null)
        {
            // The account's session ended (or never restored offline): App shows sign-in; nothing to sync until then.
            Status = SyncStatus.For(SyncResult.Of(SyncOutcome.SignedOut), _time.GetLocalNow())!;
            Changed?.Invoke();
            return;
        }
        IsBusy = true;
        var before = Status;
        Status = "Syncing…";
        Changed?.Invoke();
        try
        {
            // The open character's unsaved edits go out with this pass; an unedited one must not be marked dirty.
            if (_shell.Current?.Session.IsDirty == true) _shell.Save();
            var result = await _sync.RunAsync(() => _shell.OpenCharacterId, waitIfBusy: false, CancellationToken.None);
            if (_disposed) return; // the shell is gone (lock lost, signed out): its view model is no longer shown
            if (result.Outcome == SyncOutcome.AccountMismatch)
            {
                // Can't happen with one store per account.
                _log.LogError("Sync found this store linked to another account");
                Status = "Sync failed — see the log";
                return;
            }
            if (result.Outcome == SyncOutcome.Synced) _shell.ApplySyncResult(result);
            Status = SyncStatus.For(result, _time.GetLocalNow()) ?? before;
        }
        catch (Exception ex)
        {
            // A bug in a pass must never take the app down; local data is unaffected.
            _log.LogError(ex, "Sync pass failed");
            Status = "Sync failed — see the log";
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
