using Microsoft.Extensions.Logging;

namespace Hearthsheet.Core.Storage;

/// <summary>
/// Hands store batches to an async writer one at a time, strictly in order. A failed batch stays at the head and
/// everything after it waits: nothing is dropped or reordered. The failure shows as <see cref="Failed"/>
/// ("Not saved on this device"), and the next <see cref="Write"/> or <see cref="RetryAsync"/> tries again from the head.
/// </summary>
public class OrderedJournal(Func<IReadOnlyList<StoreRecord>, Task> write, ILogger logger) : IStoreJournal
{
    private readonly Queue<IReadOnlyList<StoreRecord>> _queue = new();
    private readonly Lock _gate = new();
    private Task _pump = Task.CompletedTask;
    private bool _pumping;

    /// <summary>The head batch failed to write; it and everything after it are only in memory.</summary>
    public bool Failed { get; private set; }

    /// <summary>Raised when <see cref="Failed"/> changes. A throwing handler is logged and never stops the writes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Enqueues and returns; never throws (see <see cref="IStoreJournal"/>).</summary>
    public void Write(IReadOnlyList<StoreRecord> batch)
    {
        lock (_gate) _queue.Enqueue(batch);
        Pump();
    }

    /// <summary>Retries from the head; completes like <see cref="FlushAsync"/>.</summary>
    public Task RetryAsync() => Pump();

    /// <summary>Completes when the queue is empty or the head batch has failed.</summary>
    public Task FlushAsync()
    {
        lock (_gate) return _pump;
    }

    private Task Pump()
    {
        lock (_gate)
        {
            if (_pumping || _queue.Count == 0) return _pump;
            _pumping = true;
            return _pump = RunAsync();
        }
    }

    // _pumping and Failed change only under _gate, together with the queue, so a Write racing the pump's exit always
    // starts a new pump. Nothing in the loop can throw (writer failures are caught, handlers are isolated), which keeps
    // _pumping from being left set.
    private async Task RunAsync()
    {
        // Off the caller's stack: the store calls Write while holding its own lock.
        await Task.Yield();
        while (true)
        {
            IReadOnlyList<StoreRecord> head;
            int queued;
            lock (_gate)
            {
                if (_queue.Count == 0) { _pumping = false; return; }
                head = _queue.Peek();
                queued = _queue.Count;
            }
            bool changed;
            try
            {
                await write(head);
            }
            catch (Exception ex)
            {
                // Surfaced through Failed, not swallowed; the batch stays queued for the retry.
                logger.LogError(ex, "A storage write failed; {Count} batch(es) are only in memory", queued);
                lock (_gate) { _pumping = false; changed = !Failed; Failed = true; }
                if (changed) RaiseStateChanged();
                return;
            }
            lock (_gate) { _queue.Dequeue(); changed = Failed; Failed = false; }
            if (changed) RaiseStateChanged();
        }
    }

    private void RaiseStateChanged()
    {
        foreach (var handler in StateChanged?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler)handler)(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A storage state handler threw");
            }
        }
    }
}
