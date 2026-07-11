namespace LeafLedger.Modules.ChartOfAccounts.Domain.Fx;

public sealed record TransactionLineFxMetadata(
    DateOnly RateDate,
    FxRateTiming RateTiming,
    FxTreatment Treatment,
    bool ClosingRevalue,
    VatFxMethod VatMethod,
    string Currency);
