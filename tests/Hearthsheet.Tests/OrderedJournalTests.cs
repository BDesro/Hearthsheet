using Hearthsheet.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hearthsheet.Tests;

public class OrderedJournalTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class FakeWriter
    {
        private int _inFlight;
        public List<string> Written { get; } = [];
        public int Attempts;
        public int FailNext;
        public int MaxInFlight;
        public TaskCompletionSource? Gate;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task Write(IReadOnlyList<StoreRecord> batch)
        {
            MaxInFlight = Math.Max(MaxInFlight, Interlocked.Increment(ref _inFlight));
            try
            {
                Interlocked.Increment(ref Attempts);
                Started.TrySetResult();
                if (Gate is { } gate) await gate.Task;
                await Task.Yield();
                if (FailNext > 0) { FailNext--; throw new InvalidOperationException("disk full"); }
                lock (Written) Written.AddRange(batch.Select(r => r.Key));
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    private static (OrderedJournal, FakeWriter) Create()
    {
        var writer = new FakeWriter();
        return (new OrderedJournal(writer.Write, NullLogger.Instance), writer);
    }

    private static StoreRecord[] Batch(string key) => [new StoreRecord(key, "{}")];

    [Fact]
    public async Task Failure_KeepsTheHeadAndOrder_UntilRetry()
    {
        var (journal, writer) = Create();
        var changes = new List<bool>();
        journal.StateChanged += (_, _) => changes.Add(journal.Failed);
        writer.FailNext = 1;

        journal.Write(Batch("a"));
        await journal.FlushAsync().WaitAsync(Timeout); // completes although the head failed
        Assert.True(journal.Failed);
        Assert.Empty(writer.Written);

        await journal.RetryAsync().WaitAsync(Timeout);
        Assert.False(journal.Failed);
        Assert.Equal(["a"], writer.Written);
        Assert.Equal([true, false], changes);
    }

    [Fact]
    public async Task NextWrite_RetriesTheFailedHeadFirst()
    {
        var (journal, writer) = Create();
        writer.FailNext = 1;
        journal.Write(Batch("a"));
        await journal.FlushAsync().WaitAsync(Timeout);
        Assert.True(journal.Failed);

        journal.Write(Batch("b"));
        await journal.FlushAsync().WaitAsync(Timeout);
        Assert.False(journal.Failed);
        Assert.Equal(["a", "b"], writer.Written);
    }

    [Fact]
    public async Task LaterBatches_WaitBehindAFailedHead()
    {
        var (journal, writer) = Create();
        writer.FailNext = 1;
        writer.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        journal.Write(Batch("a"));
        await writer.Started.Task.WaitAsync(Timeout);
        journal.Write(Batch("b"));
        journal.Write(Batch("c"));
        writer.Gate.SetResult();
        await journal.FlushAsync().WaitAsync(Timeout);

        Assert.True(journal.Failed);
        Assert.Empty(writer.Written);
        Assert.Equal(1, writer.Attempts); // b and c were not tried past the failed a

        await journal.RetryAsync().WaitAsync(Timeout);
        Assert.Equal(["a", "b", "c"], writer.Written);
    }

    [Fact]
    public async Task WriteDuringAnInFlightWrite_IsQueuedInOrder_OneAtATime()
    {
        var (journal, writer) = Create();
        writer.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        journal.Write(Batch("a"));
        await writer.Started.Task.WaitAsync(Timeout);
        journal.Write(Batch("b"));
        writer.Gate.SetResult();
        await journal.FlushAsync().WaitAsync(Timeout);

        Assert.Equal(["a", "b"], writer.Written);
        Assert.Equal(1, writer.MaxInFlight);
    }

    [Fact]
    public async Task ThrowingStateHandler_NeitherStopsWritesNorFlipsFailed()
    {
        var (journal, writer) = Create();
        journal.StateChanged += (_, _) => throw new InvalidOperationException("bad subscriber");
        writer.FailNext = 1;

        journal.Write(Batch("a"));
        await journal.FlushAsync().WaitAsync(Timeout);
        Assert.True(journal.Failed);

        journal.Write(Batch("b")); // success clears Failed, and the handler throws again
        await journal.FlushAsync().WaitAsync(Timeout);
        Assert.False(journal.Failed);

        journal.Write(Batch("c"));
        await journal.FlushAsync().WaitAsync(Timeout);
        Assert.Equal(["a", "b", "c"], writer.Written);
        Assert.False(journal.Failed);
    }

    [Fact]
    public async Task Write_NeverThrows_EvenWhenTheWriterThrowsSynchronously()
    {
        var journal = new OrderedJournal(_ => throw new InvalidOperationException("sync"), NullLogger.Instance);
        journal.Write(Batch("a"));
        await journal.FlushAsync().WaitAsync(Timeout);
        Assert.True(journal.Failed);
    }

    [Fact]
    public async Task Close_FinishesTheBatchInFlight_ThenNeverWritesAgain()
    {
        var (journal, writer) = Create();
        writer.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        journal.Write(Batch("a"));
        journal.Write(Batch("b"));
        await writer.Started.Task.WaitAsync(Timeout);

        journal.Close();
        writer.Gate.SetResult();
        await journal.FlushAsync().WaitAsync(Timeout);
        journal.Write(Batch("c"));
        await journal.RetryAsync().WaitAsync(Timeout);
        await journal.FlushAsync().WaitAsync(Timeout);

        Assert.Equal(["a"], writer.Written);
        Assert.Equal(1, writer.Attempts);
    }

    [Fact]
    public async Task Close_AfterAFailure_RetryDoesNotWrite()
    {
        var (journal, writer) = Create();
        writer.FailNext = 1;
        journal.Write(Batch("a"));
        await journal.FlushAsync().WaitAsync(Timeout);

        journal.Close();
        await journal.RetryAsync().WaitAsync(Timeout);

        Assert.Empty(writer.Written);
        Assert.Equal(1, writer.Attempts);
    }
}
