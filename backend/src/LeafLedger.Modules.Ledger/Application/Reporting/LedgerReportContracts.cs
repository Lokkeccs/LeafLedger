namespace LeafLedger.Modules.Ledger.Application.Reporting;

public interface ILedgerReportService
{
    Task<TrialBalanceReport> GetTrialBalanceAsync(Guid spaceId, CancellationToken cancellationToken = default);

    Task<BalanceSheetReport> GetBalanceSheetAsync(Guid spaceId, CancellationToken cancellationToken = default);

    Task<IncomeStatementReport> GetIncomeStatementAsync(Guid spaceId, CancellationToken cancellationToken = default);

    Task<IntegrityReport> GetIntegrityAsync(Guid spaceId, CancellationToken cancellationToken = default);
}

public sealed record TrialBalanceRow(
    Guid AccountId,
    int AccountCode,
    string AccountName,
    string AccountKind,
    long BaseBalanceMinor);

public sealed record TrialBalanceReport(
    Guid SpaceId,
    IReadOnlyList<TrialBalanceRow> Lines,
    long TotalBaseBalanceMinor);

public sealed record ReportLine(
    Guid? AccountId,
    int? AccountCode,
    string Name,
    string AccountKind,
    long AmountMinor,
    bool IsDerived);

public sealed record BalanceSheetReport(
    Guid SpaceId,
    IReadOnlyList<ReportLine> Lines,
    long CurrentResultMinor);

public sealed record IncomeStatementReport(
    Guid SpaceId,
    IReadOnlyList<ReportLine> Lines,
    long NetResultMinor);

public sealed record IntegrityReport(
    Guid SpaceId,
    string Algorithm,
    string Version,
    int LineCount,
    string TrialBalanceHash,
    bool Balanced);