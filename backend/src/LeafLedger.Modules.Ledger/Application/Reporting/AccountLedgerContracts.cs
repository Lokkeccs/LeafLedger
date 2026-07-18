namespace LeafLedger.Modules.Ledger.Application.Reporting;

public interface IAccountLedgerService
{
    Task<AccountLedgerReport> GetAccountLedgerAsync(
        Guid spaceId,
        Guid accountId,
        DateOnly? from,
        DateOnly? through,
        CancellationToken cancellationToken = default);
}

public sealed record AccountLedgerLine(
    Guid EntryId,
    long EntryNo,
    DateOnly Date,
    string? Description,
    string? Reference,
    long AmountMinor,
    long BaseAmountMinor,
    string LineCurrency,
    long RunningBalanceMinor);

public sealed record AccountLedgerReport(
    Guid SpaceId,
    Guid AccountId,
    int AccountCode,
    string AccountName,
    string AccountKind,
    string AccountCurrency,
    long OpeningBalanceMinor,
    long ClosingBalanceMinor,
    IReadOnlyList<AccountLedgerLine> Lines);