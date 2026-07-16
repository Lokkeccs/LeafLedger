using System.Diagnostics.Metrics;
using LeafLedger.Modules.Ledger.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public sealed class SpaceInvalidationMetricsTests
{
    [Fact]
    public void Queue_records_enqueued_and_coalesced_topic_counts()
    {
        using var services = new ServiceCollection()
            .AddMetrics()
            .BuildServiceProvider();
        using var listener = new MeterListener();
        var measurements = new Dictionary<string, long>(StringComparer.Ordinal);
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == "LeafLedger.Realtime")
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
            measurements[instrument.Name] = measurements.GetValueOrDefault(instrument.Name) + measurement);
        listener.Start();

        var metrics = new SpaceInvalidationMetrics(services.GetRequiredService<IMeterFactory>());
        var queue = new SpaceInvalidationQueue(metrics);
        var spaceId = Guid.NewGuid();

        Assert.True(queue.TryEnqueue(spaceId, InvalidationTopics.PostingTopics));
        Assert.False(queue.TryEnqueue(spaceId, InvalidationTopics.PostingTopics));

        Assert.Equal(2, measurements["leafledger.realtime.invalidation.enqueued"]);
        Assert.Equal(2, measurements["leafledger.realtime.invalidation.coalesced"]);
    }

    [Fact]
    public void Metrics_expose_all_realtime_counters()
    {
        using var services = new ServiceCollection()
            .AddMetrics()
            .BuildServiceProvider();
        using var listener = new MeterListener();
        var measurements = new HashSet<string>(StringComparer.Ordinal);
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == "LeafLedger.Realtime")
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => measurements.Add(instrument.Name));
        listener.Start();

        var metrics = new SpaceInvalidationMetrics(services.GetRequiredService<IMeterFactory>());
        metrics.RecordEnqueued();
        metrics.RecordCoalesced();
        metrics.RecordPing();

        Assert.Contains("leafledger.realtime.invalidation.enqueued", measurements);
        Assert.Contains("leafledger.realtime.invalidation.coalesced", measurements);
        Assert.Contains("leafledger.realtime.invalidation.pings", measurements);
    }
}
