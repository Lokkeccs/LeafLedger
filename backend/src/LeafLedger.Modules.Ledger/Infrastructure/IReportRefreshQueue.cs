namespace LeafLedger.Modules.Ledger.Infrastructure;

#pragma warning disable CA1711
public interface IReportRefreshQueue
{
    bool TryEnqueue(Guid spaceId);
    Task<ReportRefreshBatch> ReadBatchAsync(TimeSpan debounceWindow, CancellationToken cancellationToken);
}
#pragma warning restore CA1711

public sealed record ReportRefreshBatch(IReadOnlyCollection<Guid> SpaceIds, DateTimeOffset EarliestEnqueuedAt);