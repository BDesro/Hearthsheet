using Hearthsheet.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Hearthsheet.Web.Storage;

/// <summary>
/// Writes store batches to one IndexedDB database, one transaction per batch, strictly in order.
/// A failed batch stays at the head and everything after it waits: nothing is dropped or reordered. The failure shows
/// as <see cref="Failed"/> ("Not saved on this device"), and the next <see cref="Write"/> or <see cref="RetryAsync"/>
/// tries again from the head.
/// </summary>
public sealed class IndexedDbJournal(IJSObjectReference js, string dbName, ILogger logger) : IStoreJournal
{
    private readonly Queue<IReadOnlyList<StoreRecord>> _queue = new();
    private readonly Lock _gate = new();
    private Task _pump = Task.CompletedTask;
    private bool _pumping;

    public string DbName { get; } = dbName;

    /// <summary>The head batch failed to write; it and everything after it are only in memory.</summary>
    public bool Failed { get; private set; }

    /// <summary>Raised when <see cref="Failed"/> changes.</summary>
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

    private async Task RunAsync()
    {
        // Off the caller's stack: the store calls Write while holding its own lock.
        await Task.Yield();
        while (true)
        {
            IReadOnlyList<StoreRecord> head;
            lock (_gate)
            {
                if (_queue.Count == 0) { _pumping = false; return; }
                head = _queue.Peek();
            }
            try
            {
                await js.InvokeVoidAsync("idbWrite", DbName, head);
            }
            catch (Exception ex)
            {
                // Surfaced through Failed, not swallowed; the batch stays queued for the retry.
                logger.LogError(ex, "Writing to {Db} failed; {Count} batch(es) are only in memory", DbName, QueueCount());
                lock (_gate) _pumping = false;
                SetFailed(true);
                return;
            }
            lock (_gate) _queue.Dequeue();
            SetFailed(false);
        }
    }

    private int QueueCount()
    {
        lock (_gate) return _queue.Count;
    }

    private void SetFailed(bool failed)
    {
        if (Failed == failed) return;
        Failed = failed;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
