using LeafLedger.Modules.ChartOfAccounts.Domain.Accounts;

namespace LeafLedger.Modules.ChartOfAccounts.Domain.CurrencyPolicy;

public static class CurrencyPolicyEvaluator
{
    public static CurrencyPolicy Resolve(AccountKind kind) => kind is AccountKind.Income or AccountKind.Expense
        ? CurrencyPolicy.Any
        : CurrencyPolicy.Fixed;

    public static IReadOnlyList<CurrencyPolicyIssue> Evaluate(
        IEnumerable<CurrencyPolicyAccount> accounts,
        IEnumerable<CurrencyPolicyReference> references)
    {
        var accountById = accounts.ToDictionary(account => account.Id);
        var issues = new List<CurrencyPolicyIssue>();

        foreach (var reference in references)
        {
            if (!accountById.TryGetValue(reference.AccountId, out var account) ||
                Resolve(account.Kind) == CurrencyPolicy.Any)
            {
                continue;
            }

            var accountCurrency = Normalize(account.Currency);
            var transactionCurrency = Normalize(reference.TransactionCurrency);
            if (accountCurrency.Length == 0 || transactionCurrency.Length == 0)
            {
                continue;
            }

            if (!string.Equals(accountCurrency, transactionCurrency, StringComparison.Ordinal))
            {
                issues.Add(new CurrencyPolicyIssue(
                    reference.AccountId,
                    accountCurrency,
                    transactionCurrency,
                    "currency-not-allowed",
                    reference.Source));
            }
        }

        return issues;
    }

    private static string Normalize(string? currency) => (currency ?? string.Empty).Trim().ToUpperInvariant();
}
