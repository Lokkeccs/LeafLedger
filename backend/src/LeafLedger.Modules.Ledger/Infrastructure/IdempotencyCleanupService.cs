using Npgsql;
using Microsoft.Extensions.Hosting;

namespace LeafLedger.Modules.Ledger.Infrastructure;

public sealed class IdempotencyCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private readonly string _connectionString;

    public IdempotencyCleanupService(string connectionString) => _connectionString = connectionString;

    public static async Task<int> RunCleanupPassAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT delete_expired_idempotency_keys();",
            connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunCleanupPassAsync(_connectionString, stoppingToken).ConfigureAwait(false);
        }
    }
}