using Hearthsheet.Cloud;
using Hearthsheet.Presentation;
using Hearthsheet.Web.Components;
using Hearthsheet.Web.Storage;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Hearthsheet.Web;

public partial class App
{
    private TabLock? _lock;
    private BrowserStorage? _storage;
    private SupabaseAuth? _auth;
    private AccountSession? _account;
    private string? _busy;
    private string? _error;
    private int? _askSignOut;
    private bool _signOutBusy;
    private string? _signOutError;

    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private ILoggerFactory Loggers { get; set; } = null!;
    [Inject] private SupabaseHttp Http { get; set; } = null!;
    [Inject] private WebDialogs Dialogs { get; set; } = null!;

    private ILogger Log => Loggers.CreateLogger<App>();

    // Gate order (spec goal 3): 1 self-check → 2 tab lock → 3 load hearthsheet-app → 4 auth.Restore() →
    // 5 WebStartup.Choose → 6 OpenAccountAsync → 7 per-account CloudSync (Task 11: ShellViewModel) → 8 shell.
    // Without a session or a last account, only sign-in shows. Storage is opened only while this tab holds the lock.
    protected override async Task OnInitializedAsync()
    {
        if (!SelfCheckPassed) return;
        try
        {
            var tabLock = await TabLock.CreateAsync(JS, Loggers.CreateLogger<TabLock>());
            tabLock.Changed += () => InvokeAsync(StateHasChanged);
            tabLock.ReleaseRequested += DropStorageAsync;
            tabLock.PageHidden += () =>
            {
                _account?.Save(); // the journal starts writing at once
                _ = _account?.WebSync?.PageHiddenAsync(); // then flush and sync, not awaited
            };
            _lock = tabLock;
            if (await tabLock.AcquireAsync()) await OpenStorageAsync();
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Starting up failed");
            _error = "Hearthsheet couldn't start in this browser.";
        }
    }

    // Steps 3–8; also after a take-over, so this tab sees what the other tab saved. Nothing editable renders until the
    // account's store has loaded.
    private async Task OpenStorageAsync()
    {
        _error = null;
        try
        {
            var storage = await BrowserStorage.CreateAsync(JS, Loggers);
            var auth = new SupabaseAuth(Http, storage.Secrets, sessionSecrets: storage.SessionSecrets);
            var last = storage.ReadLastAccount();
            AccountSession? account = null;
            if (WebStartup.Choose(auth.Restore(), auth.UserId, last) is AccountChoice.Open open)
                account = await AccountSession.OpenAsync(open.UserId, auth.Email ?? last?.Email, storage, Http, auth, Loggers);

            // The lock went while loading (released or stolen): keep nothing.
            if (_lock!.IsReadOnly)
            {
                account?.Close();
                storage.Close();
                return;
            }
            (_storage, _auth, _account) = (storage, auth, account);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Opening this browser's storage failed");
            _error = "Couldn't open this browser's storage.";
        }
    }

    // After every successful sign-in. The same account just resumes sync on its open store; another account gets its
    // own store, and the old one is saved, flushed and closed, never touched again (spec 7a).
    private async Task OnSignedInAsync()
    {
        var (storage, auth) = (_storage!, _auth!);
        var userId = auth.UserId!;

        // Another account: close the old store first. Closing a journal whose head write failed would drop edits that
        // exist only in memory, so then stay on the old account (signed out, so its store and the session agree) until
        // "Try again" has saved them. _busy hides the shell, so nothing is edited between the flush and the close.
        if (_account is { } current && current.UserId != userId)
        {
            _busy = "Opening your characters…";
            await current.SaveAndFlushAsync();
            if (_storage != storage || _lock!.IsReadOnly)
            {
                _busy = null; // the lock went meanwhile: DropStorageAsync has already saved and closed it
                return;
            }
            // No await between this check and Close().
            if (current.Journal.Failed)
            {
                _busy = null;
                await auth.SignOutAsync(CancellationToken.None);
                _ = Dialogs.ErrorAsync($"Not saved on this device — try again first.\n\nSome changes to {current.Email ?? "the open account"}'s " +
                    "characters couldn't be saved in this browser, so Hearthsheet stayed on that account. Use \"Try again\" " +
                    "in the status bar, then sign in again.");
                return;
            }
            _account = null;
            current.Close();
        }

        storage.WriteLastAccount(new LastAccount(userId, auth.Email));
        try
        {
            await storage.PersistOnceAsync();
        }
        catch (JSException ex)
        {
            Log.LogWarning(ex, "Asking the browser to keep storage failed");
        }

        if (_account is { } open && open.UserId == userId)
        {
            open.Email = auth.Email ?? open.Email;
            _ = open.WebSync?.RunAsync(); // signed back in: resume sync on the same store
            return;
        }

        _busy = "Opening your characters…";
        try
        {
            var account = await AccountSession.OpenAsync(userId, auth.Email, storage, Http, auth, Loggers);
            if (_storage != storage || _lock!.IsReadOnly) account.Close(); // dropped while loading
            else _account = account;
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Opening the account's store failed");
            _error = "Couldn't open your characters in this browser.";
        }
        finally
        {
            _busy = null;
        }
    }

    // Spec 7a: save, try one sync, then remove the store only if nothing is left unsynced; otherwise ask.
    private async Task SignOutAsync()
    {
        if (_account is not { } account) return;
        _busy = "Signing out…"; // hides the shell, so nothing new is edited meanwhile
        try
        {
            account.Save();
            try
            {
                await account.Sync.RunAsync(account.OpenCharacter, waitIfBusy: true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.LogWarning(ex, "The sync before signing out failed");
            }
            switch (WebStartup.PlanSignOut(account.Store.UnsyncedCount))
            {
                case SignOutPlan.RemoveStore:
                    await FinishSignOutAsync(account);
                    break;
                case SignOutPlan.AskUser ask:
                    (_askSignOut, _signOutError) = (ask.Unsynced, null);
                    break;
            }
        }
        finally
        {
            _busy = null;
        }
    }

    private async Task OnSignOutChosenAsync(SignOutDialog.SignOutChoice choice)
    {
        if (_account is not { } account) return;
        if (choice == SignOutDialog.SignOutChoice.Stay)
        {
            _askSignOut = null;
            return;
        }
        _signOutBusy = true;
        try
        {
            if (choice == SignOutDialog.SignOutChoice.Export)
            {
                using var zip = new MemoryStream();
                var skipped = account.Library.ExportAll(zip);
                await _storage!.DownloadAsync($"Hearthsheet characters {DateTime.Now:yyyy-MM-dd}.zip", "application/zip", zip.ToArray());
                if (skipped.Count > 0)
                {
                    _signOutError = $"Couldn't export {string.Join(", ", skipped)}. The rest were downloaded.";
                    return;
                }
            }
            _askSignOut = null;
            await FinishSignOutAsync(account);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Exporting before signing out failed");
            _signOutError = "Couldn't export your characters. Nothing was removed.";
        }
        finally
        {
            _signOutBusy = false;
        }
    }

    // Save → flush → end the session → close the store's journal (so nothing can recreate the database) → delete it →
    // forget the last account. Then only sign-in shows.
    private async Task FinishSignOutAsync(AccountSession account)
    {
        _busy = "Signing out…";
        var storage = _storage!;
        _account = null;
        try
        {
            await account.SaveAndFlushAsync();
            await _auth!.SignOutAsync(CancellationToken.None);
            account.Close();
            await storage.ForgetAccountAsync(account.UserId);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Removing the signed-out account's store failed");
            _error = "Signed out, but this browser may still hold that account's characters. Sign in and out again to remove them.";
        }
        finally
        {
            account.Close();
            // A tab that lost the lock meanwhile must not clear what the tab now holding it relies on.
            if (!_lock!.IsReadOnly) storage.ClearLastAccount();
            _busy = null;
        }
    }

    // The lock is going (or gone): save what's pending, then drop every storage object, so a tab without the lock holds
    // none and nothing stale can write later.
    private async Task DropStorageAsync()
    {
        var (storage, account) = (_storage, _account);
        (_storage, _auth, _account, _askSignOut) = (null, null, null, null);
        account?.WebSync?.Dispose(); // no pass may start without the lock
        if (storage is null) return;
        try
        {
            if (account is not null) await account.SaveAndFlushAsync();
            await storage.FlushAsync();
        }
        finally
        {
            account?.Close();
            storage.Close();
        }
    }
}
