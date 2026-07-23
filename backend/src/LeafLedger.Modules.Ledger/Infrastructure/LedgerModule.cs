using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LeafLedger.Modules.Ledger.Application.Posting;
using LeafLedger.Modules.Ledger.Application.Reporting;
using LeafLedger.Modules.Ledger.Application.Periods;
using LeafLedger.Modules.Ledger.Application.Accounts;

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
        services.AddMetrics();
        services.AddDbContext<LedgerDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IJournalPostingService, JournalPostingService>();
        services.AddScoped<ILedgerReportService, LedgerReportService>();
        services.AddScoped<IAccountLedgerService, AccountLedgerService>();
        services.AddScoped<IAccountCatalogService, AccountCatalogService>();
        services.AddScoped<AccountManagementService>();
        services.AddScoped<IAccountManagementService>(serviceProvider => serviceProvider.GetRequiredService<AccountManagementService>());
        services.AddScoped<IGroupCatalogService>(serviceProvider => serviceProvider.GetRequiredService<AccountManagementService>());
        services.AddScoped<IPeriodLifecycleService, PeriodLifecycleService>();
        services.AddScoped<ISpaceMembershipQuery, SpaceMembershipQuery>();
        services.AddScoped<IIdentityResolver, IdentityResolver>();
        services.AddSingleton<IdempotencyMetrics>();
        services.AddSingleton<IReportRefreshQueue, ReportRefreshQueue>();
        services.AddSingleton<ReportingRefreshMetrics>();
        services.AddSingleton<ISpaceInvalidationQueue, SpaceInvalidationQueue>();
        services.AddSingleton<SpaceInvalidationMetrics>();
        services.AddSingleton<IHostedService>(_ => new IdempotencyCleanupService(connectionString));
        services.AddSingleton(serviceProvider => new RefreshCoalescingService(
            connectionString,
            serviceProvider.GetRequiredService<IReportRefreshQueue>(),
            serviceProvider.GetRequiredService<ISpaceInvalidationQueue>(),
            serviceProvider.GetRequiredService<ReportingRefreshMetrics>()));
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<RefreshCoalescingService>());
        services.AddHostedService<InvalidationBroadcastService>();
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
