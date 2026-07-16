namespace LeafLedger.Modules.Ledger.Infrastructure;

#pragma warning disable CA1711

public interface ISpaceInvalidationQueue
{
    bool TryEnqueue(Guid spaceId, IEnumerable<string> topics);

    Task<IReadOnlyList<SpaceInvalidationBatch>> ReadBatchAsync(
        TimeSpan debounceWindow,
        CancellationToken cancellationToken = default);
}

public sealed record SpaceInvalidationBatch(Guid SpaceId, IReadOnlyList<string> Topics);

#pragma warning restore CA1711
