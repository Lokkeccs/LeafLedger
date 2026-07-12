using System.Diagnostics.Metrics;

namespace LeafLedger.Modules.Ledger.Infrastructure;

public sealed class IdempotencyMetrics
{
    private readonly Counter<long> _collisions;

    public IdempotencyMetrics(IMeterFactory meterFactory)
    {
        _collisions = meterFactory.Create("LeafLedger.Idempotency")
            .CreateCounter<long>("leafledger.idempotency.collisions");
    }

    public void RecordCollision(Guid spaceId) =>
        _collisions.Add(1, new KeyValuePair<string, object?>("space_id", spaceId));
}