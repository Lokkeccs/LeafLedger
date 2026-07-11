namespace LeafLedger.Modules.ChartOfAccounts.Domain.Fx;

public sealed record FxPolicyOverride(
    FxTreatment? Treatment,
    FxRateTiming? RateTiming,
    bool? ClosingRevalue,
    VatFxMethod? VatMethod);
