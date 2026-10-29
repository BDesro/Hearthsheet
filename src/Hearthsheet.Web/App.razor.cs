using Hearthsheet.Web.Storage;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Hearthsheet.Web;

public partial class App
{
#if DEBUG
    // TEMPORARY Task 8 dev page, removed in Task 11.
    private static readonly Type? DevPage = typeof(Dev.DevStorage);
#else
    private static readonly Type? DevPage = null;
#endif

    private TabLock? _lock;
    private BrowserStorage? _storage;
    private string? _error;

    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private ILoggerFactory Loggers { get; set; } = null!;

    private Dictionary<string, object?> DevParameters => new() { ["Storage"] = _storage };

    // Gate order: self-check → tab lock → app storage (→ Task 10: session, account, store → shell). Storage is opened
    // only while this tab holds the lock.
    protected override async Task OnInitializedAsync()
    {
        if (!SelfCheckPassed) return;
        try
        {
            var tabLock = await TabLock.CreateAsync(JS, Loggers.CreateLogger<TabLock>());
            tabLock.Changed += () => InvokeAsync(StateHasChanged);
            tabLock.ReleaseRequested += DropStorageAsync;
            _lock = tabLock;
            if (await tabLock.AcquireAsync()) await OpenStorageAsync();
        }
        catch (Exception ex)
        {
            Loggers.CreateLogger<App>().LogError(ex, "Starting up failed");
            _error = "Hearthsheet couldn't start in this browser.";
        }
    }

    // Also after a take-over: storage is loaded fresh, so this tab sees what the other tab saved. Nothing editable
    // renders until it has loaded.
    private async Task OpenStorageAsync()
    {
        try
        {
            var storage = await BrowserStorage.CreateAsync(JS, Loggers);
            // The lock went while loading (released or stolen): keep nothing.
            if (_lock!.IsReadOnly) storage.Close();
            else _storage = storage;
        }
        catch (Exception ex)
        {
            Loggers.CreateLogger<App>().LogError(ex, "Opening this browser's storage failed");
            _error = "Couldn't open this browser's storage.";
        }
    }

    // The lock is going (or gone): save what's pending, then drop every storage object, so a tab without the lock holds
    // none and nothing stale can write later. Task 11 adds the shell's Save() before the flush.
    private async Task DropStorageAsync()
    {
        if (_storage is not { } storage) return;
        _storage = null;
        try
        {
            await storage.FlushAsync();
        }
        finally
        {
            storage.Close();
        }
    }
}
