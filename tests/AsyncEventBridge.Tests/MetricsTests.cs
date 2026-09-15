using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace AsyncEventBridge.Tests;

public sealed class MetricsTests
{
    private const string MeterName = "AsyncEventBridge";
    private const string WaitOutcomesInstrumentName = "asynceventbridge.event_wait.outcomes";
    private const string StreamDropsInstrumentName = "asynceventbridge.event_stream.dropped";

    [Fact]
    public async Task WaitOutcomesExposeAllTerminalStates()
    {
        using var capture = new MetricCapture();
        var source = new MetricEventSource();

        var success = EventAwaiter.WaitAsync<int>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler);
        source.Raise(42);
        Assert.Equal(42, await success);

        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            EventAwaiter.WaitAsync<int>(
                handler => source.Changed += handler,
                handler => source.Changed -= handler,
                cancellationToken: cancellationSource.Token));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            EventAwaiter.WaitAsync<int>(
                handler => source.Changed += handler,
                handler => source.Changed -= handler,
                timeout: TimeSpan.Zero));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EventAwaiter.WaitAsync<int>(
                _ => throw new InvalidOperationException("subscribe failed"),
                _ => { }));

        Assert.Contains(capture.Measurements, measurement =>
            measurement.InstrumentName == WaitOutcomesInstrumentName &&
            measurement.TagName == "asynceventbridge.wait.outcome" &&
            measurement.TagValue == "success");
        Assert.Contains(capture.Measurements, measurement =>
            measurement.InstrumentName == WaitOutcomesInstrumentName &&
            measurement.TagName == "asynceventbridge.wait.outcome" &&
            measurement.TagValue == "cancelled");
        Assert.Contains(capture.Measurements, measurement =>
            measurement.InstrumentName == WaitOutcomesInstrumentName &&
            measurement.TagName == "asynceventbridge.wait.outcome" &&
            measurement.TagValue == "timeout");
        Assert.Contains(capture.Measurements, measurement =>
            measurement.InstrumentName == WaitOutcomesInstrumentName &&
            measurement.TagName == "asynceventbridge.wait.outcome" &&
            measurement.TagValue == "faulted");
    }

    [Theory]
    [InlineData(EventStreamFullMode.DropOldest, "drop_oldest")]
    [InlineData(EventStreamFullMode.DropNewest, "drop_newest")]
    public async Task BoundedStreamDropsExposeFullMode(
        EventStreamFullMode fullMode,
        string expectedTagValue)
    {
        using var capture = new MetricCapture();
        var source = new MetricEventSource();
        var options = new EventStreamOptions
        {
            Capacity = 1,
            FullMode = fullMode,
        };

        await using var enumerator = EventStream.Create<int>(
            handler => source.Changed += handler,
            handler => source.Changed -= handler,
            options: options).GetAsyncEnumerator();

        var firstMove = enumerator.MoveNextAsync().AsTask();
        source.Raise(1);
        Assert.True(await firstMove);
        Assert.Equal(1, enumerator.Current);

        source.Raise(2);
        source.Raise(3);

        Assert.Contains(capture.Measurements, measurement =>
            measurement.InstrumentName == StreamDropsInstrumentName &&
            measurement.TagName == "asynceventbridge.stream.full_mode" &&
            measurement.TagValue == expectedTagValue);
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        internal MetricCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            {
                string? tagName = null;
                string? tagValue = null;

                foreach (var tag in tags)
                {
                    tagName = tag.Key;
                    tagValue = tag.Value?.ToString();
                    break;
                }

                Measurements.Enqueue(new MetricMeasurement(
                    instrument.Name,
                    measurement,
                    tagName,
                    tagValue));
            });

            _listener.Start();
        }

        internal ConcurrentQueue<MetricMeasurement> Measurements { get; } = new();

        public void Dispose() => _listener.Dispose();
    }

    private sealed record MetricMeasurement(
        string InstrumentName,
        long Value,
        string? TagName,
        string? TagValue);

    private sealed class MetricEventSource
    {
        internal event EventHandler<int>? Changed;

        internal void Raise(int value) => Changed?.Invoke(this, value);
    }
}
