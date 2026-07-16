using System.Threading.Channels;

namespace LeafLedger.Modules.Ledger.Infrastructure;

#pragma warning disable CA1711

public sealed class SpaceInvalidationQueue : ISpaceInvalidationQueue
{
    private readonly Channel<bool> _signals = Channel.CreateUnbounded<bool>();
    private readonly object _gate = new();
    private readonly Dictionary<Guid, HashSet<string>> _pending = new();
    private readonly SpaceInvalidationMetrics? _metrics;

    public SpaceInvalidationQueue(SpaceInvalidationMetrics? metrics = null) => _metrics = metrics;

    public bool TryEnqueue(Guid spaceId, IEnumerable<string> topics)
    {
        var knownTopics = topics
            .Where(InvalidationTopics.IsKnown)
            .ToHashSet(StringComparer.Ordinal);
        if (knownTopics.Count == 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_pending.TryGetValue(spaceId, out var pendingTopics))
            {
                pendingTopics = new HashSet<string>(StringComparer.Ordinal);
                _pending.Add(spaceId, pendingTopics);
            }

            var addedCount = 0;
            foreach (var topic in knownTopics)
            {
                if (pendingTopics.Add(topic))
                {
                    addedCount++;
                }
            }
            if (addedCount > 0)
            {
                _signals.Writer.TryWrite(true);
                _metrics?.RecordEnqueued(addedCount);
            }
            _metrics?.RecordCoalesced(knownTopics.Count - addedCount);

            return addedCount > 0;
        }
    }

    public async Task<IReadOnlyList<SpaceInvalidationBatch>> ReadBatchAsync(
        TimeSpan debounceWindow,
        CancellationToken cancellationToken = default)
    {
        await _signals.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (debounceWindow > TimeSpan.Zero)
        {
            await Task.Delay(debounceWindow, cancellationToken).ConfigureAwait(false);
        }

        lock (_gate)
        {
            var batch = _pending
                .Select(item => new SpaceInvalidationBatch(item.Key, item.Value.ToArray()))
                .ToArray();
            _pending.Clear();
            while (_signals.Reader.TryRead(out _))
            {
            }

            return batch;
        }
    }
}

#pragma warning restore CA1711
