using System.Threading.Channels;

namespace AsyncEventBridge.StressTests;

public sealed class EventStreamBridgeStressTests
{
    [Fact]
    public async Task CompletionAndCancellationRacePublishesExactlyOneTerminalOutcome()
    {
        const int iterations = 200;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var channel = Channel.CreateUnbounded<int>();
            using var cancellation = new CancellationTokenSource();
            var bridge = channel.Reader.ReadAllAsync().ToEventBridge();
            var terminalObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var terminalCount = 0;

            void ObserveTerminal()
            {
                Interlocked.Increment(ref terminalCount);
                terminalObserved.TrySetResult(true);
            }

            bridge.Completed += (_, _) => ObserveTerminal();
            bridge.Faulted += (_, _) => ObserveTerminal();
            bridge.Cancelled += (_, _) => ObserveTerminal();
            bridge.Connect(cancellation.Token);

            using var start = new Barrier(3);
            var completeTask = Task.Run(() =>
            {
                start.SignalAndWait();
                channel.Writer.TryComplete();
            });
            var cancelTask = Task.Run(() =>
            {
                start.SignalAndWait();
                cancellation.Cancel();
            });

            start.SignalAndWait();

            await Task.WhenAll(completeTask, cancelTask);
            await terminalObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await bridge.DisposeAsync();

            Assert.Equal(1, Volatile.Read(ref terminalCount));
        }
    }

    [Fact]
    public async Task DisposeAsyncAndValueRaceNeverPublishesAfterDisposeCompletes()
    {
        const int iterations = 200;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var channel = Channel.CreateUnbounded<int>();
            var bridge = channel.Reader.ReadAllAsync().ToEventBridge();
            var publishedValues = 0;

            bridge.Value += (_, _) => Interlocked.Increment(ref publishedValues);
            bridge.Connect();

            using var start = new Barrier(3);
            var writeTask = Task.Run(() =>
            {
                start.SignalAndWait();
                channel.Writer.TryWrite(1);
            });
            var disposeTask = Task.Run(async () =>
            {
                start.SignalAndWait();
                await bridge.DisposeAsync();
            });

            start.SignalAndWait();
            await Task.WhenAll(writeTask, disposeTask);

            var countAtDisposeCompletion = Volatile.Read(ref publishedValues);

            Assert.True(channel.Writer.TryWrite(2));
            Assert.Equal(countAtDisposeCompletion, Volatile.Read(ref publishedValues));
            Assert.InRange(countAtDisposeCompletion, 0, 1);
        }
    }
}
