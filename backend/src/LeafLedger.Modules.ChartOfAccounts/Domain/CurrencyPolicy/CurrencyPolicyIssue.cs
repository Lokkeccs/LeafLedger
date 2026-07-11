using LeafLedger.Modules.ChartOfAccounts.Domain.Accounts;
using LeafLedger.SharedKernel;

namespace LeafLedger.Modules.ChartOfAccounts.Domain.CurrencyPolicy;

public sealed record CurrencyPolicyIssue(
    Id<AccountTag> AccountId,
    string AccountCurrency,
    string TransactionCurrency,
    string Reason,
    string? Source);
