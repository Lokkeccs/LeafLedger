using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace LeafLedger.Modules.Ledger.Infrastructure;

public sealed class RefreshCoalescingService : BackgroundService
{
    private static readonly TimeSpan DefaultDebounceWindow = TimeSpan.FromMilliseconds(200);
    private readonly string _connectionString;
    private readonly IReportRefreshQueue _queue;
    private readonly ReportingRefreshMetrics _metrics;
    private readonly TimeSpan _debounceWindow;
    private readonly Func<CancellationToken, Task<long>> _refresh;

    public RefreshCoalescingService(
        string connectionString,
        IReportRefreshQueue queue,
        ReportingRefreshMetrics metrics,
        TimeSpan? debounceWindow = null,
        Func<CancellationToken, Task<long>>? refresh = null)
    {
        _connectionString = connectionString;
        _queue = queue;
        _metrics = metrics;
        _debounceWindow = debounceWindow ?? DefaultDebounceWindow;
        _refresh = refresh ?? (cancellationToken => RunRefreshPassAsync(_connectionString, cancellationToken));
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
            var rows = await _refresh(stoppingToken).ConfigureAwait(false);
            stopwatch.Stop();
            _metrics.RecordRefresh(stopwatch.Elapsed, DateTimeOffset.UtcNow - batch.EarliestEnqueuedAt, rows);
        }
    }
}