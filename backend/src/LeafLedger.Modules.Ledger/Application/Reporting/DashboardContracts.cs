namespace LeafLedger.Modules.Ledger.Application.Reporting;

public interface IDashboardService
{
    Task<DashboardSummaryReport> GetDashboardSummaryAsync(
        Guid spaceId,
        CancellationToken cancellationToken = default);
}

public sealed record DashboardSummaryReport(
    Guid SpaceId,
    long TotalAssetsMinor,
    long TotalLiabilitiesMinor,
    long TotalEquityMinor,
    long TotalIncomeMinor,
    long TotalExpensesMinor,
    long NetResultMinor,
    long NetWorthMinor,
    int AccountCount,
    bool Balanced);