using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Hearthsheet.Web;

/// <summary>
/// One tab at a time (spec decision 7): the Web Lock <c>hearthsheet</c>. A tab without it is read-only: its editing UI
/// is disabled and <c>hearthsheet.js</c> refuses every IndexedDB call, so nothing it does reaches storage.
/// </summary>
public sealed class TabLock
{
    private readonly IJSObjectReference _js;
    private readonly ILogger _logger;
    private readonly DotNetObjectReference<TabLock> _ref;
    private Task? _releasing;

    private TabLock(IJSObjectReference js, ILogger logger)
    {
        _js = js;
        _logger = logger;
        _ref = DotNetObjectReference.Create(this);
    }

    public static async Task<TabLock> CreateAsync(IJSRuntime runtime, ILogger<TabLock> logger) =>
        new(await runtime.InvokeAsync<IJSObjectReference>("import", "./js/hearthsheet.js"), logger);

    /// <summary>True until <see cref="AcquireAsync"/> or <see cref="TakeOverAsync"/> gets the lock, and again once it is released or stolen.</summary>
    public bool IsReadOnly { get; private set; } = true;

    /// <summary>
    /// Another tab asked for the lock. Every handler runs in turn (save, then flush) before the lock is released. A
    /// throwing handler is logged and the release goes ahead: the other tab takes the lock after 3 s anyway.
    /// </summary>
    public event Func<Task>? ReleaseRequested;

    /// <summary>Raised when <see cref="IsReadOnly"/> changes from the JS side (released to, or stolen by, another tab).</summary>
    public event Action? Changed;

    /// <summary>Takes the lock if no other tab holds it.</summary>
    public async Task<bool> AcquireAsync()
    {
        var acquired = await _js.InvokeAsync<bool>("lockAcquire", _ref);
        IsReadOnly = !acquired;
        return acquired;
    }

    /// <summary>Asks the holder to save and let go; takes the lock by force if it hasn't within 3 s.</summary>
    public async Task TakeOverAsync()
    {
        await _js.InvokeAsync<bool>("lockTakeOver");
        IsReadOnly = false;
    }

    [JSInvokable("ReleaseRequested")]
    public Task OnReleaseRequested() =>
        _releasing is { IsCompleted: false } ? _releasing : _releasing = ReleaseAsync();

    [JSInvokable]
    public void LockLost()
    {
        _logger.LogWarning("Another tab took Hearthsheet over without waiting for a save");
        SetReadOnly();
    }

    private async Task ReleaseAsync()
    {
        // Editing stops first so nothing new is made while saving.
        SetReadOnly();
        foreach (var handler in ReleaseRequested?.GetInvocationList() ?? [])
        {
            try
            {
                await ((Func<Task>)handler)();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Saving before handing over to another tab failed");
            }
        }
        await _js.InvokeVoidAsync("lockRelease");
    }

    private void SetReadOnly()
    {
        if (IsReadOnly) return;
        IsReadOnly = true;
        Changed?.Invoke();
    }
}
