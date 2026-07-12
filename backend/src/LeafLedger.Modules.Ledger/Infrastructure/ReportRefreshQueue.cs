using System.Threading.Channels;

namespace LeafLedger.Modules.Ledger.Infrastructure;

#pragma warning disable CA1711
public sealed class ReportRefreshQueue : IReportRefreshQueue
{
    private readonly Channel<Guid> _signals = Channel.CreateUnbounded<Guid>();
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DateTimeOffset> _pending = new();

    public bool TryEnqueue(Guid spaceId)
    {
        lock (_gate)
        {
            if (_pending.ContainsKey(spaceId))
            {
                return false;
            }

            _pending.Add(spaceId, DateTimeOffset.UtcNow);
            return _signals.Writer.TryWrite(spaceId);
        }
    }

    public async Task<ReportRefreshBatch> ReadBatchAsync(TimeSpan debounceWindow, CancellationToken cancellationToken)
    {
        await _signals.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        await Task.Delay(debounceWindow, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            var batch = new ReportRefreshBatch(
                _pending.Keys.ToArray(),
                _pending.Values.Min());
            _pending.Clear();
            while (_signals.Reader.TryRead(out _))
            {
            }

            return batch;
        }
    }
}
#pragma warning restore CA1711