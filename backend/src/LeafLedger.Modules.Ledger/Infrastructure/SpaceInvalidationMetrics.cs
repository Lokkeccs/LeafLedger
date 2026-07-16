using System.Diagnostics.Metrics;

namespace LeafLedger.Modules.Ledger.Infrastructure;

public sealed class SpaceInvalidationMetrics
{
    private readonly Counter<long> _enqueued;
    private readonly Counter<long> _coalesced;
    private readonly Counter<long> _pings;

    public SpaceInvalidationMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("LeafLedger.Realtime");
        _enqueued = meter.CreateCounter<long>("leafledger.realtime.invalidation.enqueued", "signals");
        _coalesced = meter.CreateCounter<long>("leafledger.realtime.invalidation.coalesced", "signals");
        _pings = meter.CreateCounter<long>("leafledger.realtime.invalidation.pings", "pings");
    }

    public void RecordEnqueued(long count = 1) => _enqueued.Add(count);

    public void RecordCoalesced(long count = 1) => _coalesced.Add(count);

    public void RecordPing() => _pings.Add(1);
}
