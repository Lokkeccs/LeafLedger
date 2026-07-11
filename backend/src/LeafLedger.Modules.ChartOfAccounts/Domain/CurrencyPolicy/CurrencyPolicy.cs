using LeafLedger.Modules.ChartOfAccounts.Domain.Accounts;
using LeafLedger.SharedKernel;

namespace LeafLedger.Modules.ChartOfAccounts.Domain.CurrencyPolicy;

public enum CurrencyPolicy
{
    Any,
    Fixed,
}

public sealed record CurrencyPolicyAccount(Id<AccountTag> Id, string? Currency, AccountKind Kind);

public sealed record CurrencyPolicyReference(
    Id<AccountTag> AccountId,
    string? TransactionCurrency,
    string? Source = null);
