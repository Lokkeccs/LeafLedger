using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LeafLedger.Modules.Ledger.Infrastructure;

public sealed class RefreshCoalescingService : BackgroundService
{
    private static readonly TimeSpan DefaultDebounceWindow = TimeSpan.FromMilliseconds(200);
    private static readonly Action<ILogger, int, Exception?> RefreshFailed =
        LoggerMessage.Define<int>(LogLevel.Error, new EventId(1, "ReportRefreshFailed"), "Ledger report refresh failed for {SpaceCount} spaces.");
    private readonly string _connectionString;
    private readonly IReportRefreshQueue _queue;
    private readonly ISpaceInvalidationQueue _invalidationQueue;
    private readonly ReportingRefreshMetrics _metrics;
    private readonly TimeSpan _debounceWindow;
    private readonly Func<CancellationToken, Task<long>> _refresh;
    private readonly ILogger<RefreshCoalescingService> _logger;

    public RefreshCoalescingService(
        string connectionString,
        IReportRefreshQueue queue,
        ISpaceInvalidationQueue invalidationQueue,
        ReportingRefreshMetrics metrics,
        TimeSpan? debounceWindow = null,
        Func<CancellationToken, Task<long>>? refresh = null,
        ILogger<RefreshCoalescingService>? logger = null)
    {
        _connectionString = connectionString;
        _queue = queue;
        _invalidationQueue = invalidationQueue;
        _metrics = metrics;
        _debounceWindow = debounceWindow ?? DefaultDebounceWindow;
        _refresh = refresh ?? (cancellationToken => RunRefreshPassAsync(_connectionString, cancellationToken));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RefreshCoalescingService>.Instance;
    }

    public static async Task<long> RunRefreshPassAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var refresh = new NpgsqlCommand("SELECT refresh_trial_balance_mat();", connection);
        return Convert.ToInt64(await refresh.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var batch = await _queue.ReadBatchAsync(_debounceWindow, stoppingToken).ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var rows = await _refresh(stoppingToken).ConfigureAwait(false);
                stopwatch.Stop();
                foreach (var spaceId in batch.SpaceIds)
                {
                    _invalidationQueue.TryEnqueue(spaceId, new[] { InvalidationTopics.TrialBalance });
                }
                _metrics.RecordRefresh(stopwatch.Elapsed, DateTimeOffset.UtcNow - batch.EarliestEnqueuedAt, rows);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                RefreshFailed(_logger, batch.SpaceIds.Count, exception);
                throw;
            }
        }
    }
}