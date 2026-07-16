using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace LeafLedger.Modules.Ledger.Infrastructure;

public sealed class InvalidationBroadcastService : BackgroundService
{
    private static readonly TimeSpan DefaultDebounceWindow = TimeSpan.FromMilliseconds(200);
    private readonly ISpaceInvalidationQueue _queue;
    private readonly IHubContext<SpaceInvalidationHub> _hubContext;
    private readonly SpaceInvalidationMetrics _metrics;
    private readonly TimeSpan _debounceWindow;

    public InvalidationBroadcastService(
        ISpaceInvalidationQueue queue,
        IHubContext<SpaceInvalidationHub> hubContext,
        SpaceInvalidationMetrics metrics,
        TimeSpan? debounceWindow = null)
    {
        _queue = queue;
        _hubContext = hubContext;
        _metrics = metrics;
        _debounceWindow = debounceWindow ?? DefaultDebounceWindow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var batch = await _queue.ReadBatchAsync(_debounceWindow, stoppingToken).ConfigureAwait(false);
            foreach (var item in batch)
            {
                foreach (var topic in item.Topics)
                {
                    await _hubContext.Clients
                        .Group(SpaceInvalidationHub.GroupName(item.SpaceId))
                        .SendAsync(
                            "spaceInvalidated",
                            new { spaceId = item.SpaceId, topic },
                            stoppingToken)
                        .ConfigureAwait(false);
                    _metrics.RecordPing();
                }
            }
        }
    }
}
