using System.Collections.Frozen;
using LeafLedger.Modules.ChartOfAccounts.Domain.Accounts;

namespace LeafLedger.Modules.ChartOfAccounts.Domain.Fx;

public static class FxPolicyResolver
{
    private static readonly FrozenSet<string> MonetaryGroups = new[]
    {
        "bank accounts",
        "cash & cash equivalents",
        "savings & emergency fund",
        "receivables",
        "accounts payable",
        "long-term debt",
        "loans & mortgages",
        "accrued liabilities",
        "tax liabilities",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> HistoricalGroups = new[]
    {
        "financial investments",
        "investments",
        "inventory",
        "prepaid expenses",
        "prepaid expense",
        "real estate",
        "real estate & property",
        "other fixed assets",
        "intangible assets",
        "deferred revenue",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> CurrentValueGroups = new[]
    {
        "commodities",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static FxPolicy InferBusiness(AccountKind kind, string group)
    {
        var normalizedGroup = group.Trim().ToLowerInvariant();
        if (CurrentValueGroups.Contains(normalizedGroup))
        {
            return new FxPolicy(
                FxTreatment.CurrentValue,
                FxRateTiming.ValuationDate,
                true,
                VatFxMethod.SpaceDefault);
        }

        if (MonetaryGroups.Contains(normalizedGroup))
        {
            return MonetaryPolicy();
        }

        if (HistoricalGroups.Contains(normalizedGroup))
        {
            return HistoricalPolicy();
        }

        return kind == AccountKind.Liability ? MonetaryPolicy() : HistoricalPolicy();
    }

    public static FxPolicy ResolveGroup(
        AppPurpose purpose,
        AccountKind kind,
        string group,
        FxPolicyOverride? groupOverride = null)
    {
        var fallback = purpose == AppPurpose.Business
            ? InferBusiness(kind, group)
            : HistoricalPolicy();
        return Apply(fallback, groupOverride);
    }

    public static FxPolicy ResolveAccount(
        AppPurpose purpose,
        FxAccount account,
        FxPolicyOverride? groupOverride = null)
    {
        var groupPolicy = ResolveGroup(purpose, account.Kind, account.Group, groupOverride);
        return Apply(groupPolicy, account.Override);
    }

    public static TransactionLineFxMetadata BuildTransactionLineMetadata(
        AppPurpose purpose,
        FxAccount account,
        DateOnly transactionDate)
    {
        var policy = ResolveAccount(purpose, account);
        return new TransactionLineFxMetadata(
            transactionDate,
            policy.RateTiming,
            policy.Treatment,
            policy.ClosingRevalue,
            policy.VatMethod,
            account.Currency ?? string.Empty);
    }

    private static FxPolicy Apply(FxPolicy fallback, FxPolicyOverride? policyOverride) => new(
        policyOverride?.Treatment ?? fallback.Treatment,
        policyOverride?.RateTiming ?? fallback.RateTiming,
        policyOverride?.ClosingRevalue ?? fallback.ClosingRevalue,
        policyOverride?.VatMethod ?? fallback.VatMethod);

    private static FxPolicy MonetaryPolicy() => new(
        FxTreatment.Monetary,
        FxRateTiming.TransactionDate,
        true,
        VatFxMethod.SpaceDefault);

    private static FxPolicy HistoricalPolicy() => new(
        FxTreatment.Historical,
        FxRateTiming.TransactionDate,
        false,
        VatFxMethod.SpaceDefault);
}
