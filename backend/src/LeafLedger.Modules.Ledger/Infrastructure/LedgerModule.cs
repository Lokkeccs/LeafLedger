using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeafLedger.Modules.Ledger.Infrastructure;

/// <summary>
/// Composition-root entry points for the Ledger module. All Entity Framework usage is kept
/// inside this Infrastructure namespace so the Host (and any non-Infrastructure code) never
/// takes a direct EF Core dependency — the module-boundary architecture rule (Part 3 §5).
/// </summary>
public static class LedgerModule
{
    /// <summary>Registers the <see cref="LedgerDbContext"/> against the given Postgres
    /// connection string.</summary>
    public static IServiceCollection AddLedgerModule(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddDbContext<LedgerDbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }

    /// <summary>Applies any pending migrations for the Ledger schema. Intended for local
    /// dev / CI startup; production migrations run through the deploy pipeline.</summary>
    public static async Task MigrateLedgerAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
