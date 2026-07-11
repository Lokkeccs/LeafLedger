using LeafLedger.Modules.ChartOfAccounts.Domain.Accounts;

namespace LeafLedger.Modules.ChartOfAccounts.Domain.Fx;

public enum AppPurpose
{
    Business,
    Personal,
}

public enum FxTreatment
{
    Monetary,
    Historical,
    CurrentValue,
}

public enum FxRateTiming
{
    TransactionDate,
    SettlementDate,
    ValuationDate,
}

public enum VatFxMethod
{
    SpaceDefault,
    Daily,
    MonthlyAverage,
}

public sealed record FxPolicy(
    FxTreatment Treatment,
    FxRateTiming RateTiming,
    bool ClosingRevalue,
    VatFxMethod VatMethod);

public sealed record FxAccount(
    AccountKind Kind,
    string Group,
    string? Currency = null,
    FxPolicyOverride? Override = null);
