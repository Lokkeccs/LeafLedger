using LeafLedger.SharedKernel;

namespace LeafLedger.Modules.Ledger.Domain.Journal;

public sealed class JournalLine
{
    private JournalLine(
        Guid accountId,
        long amountMinor,
        CurrencyCode currency,
        long baseAmountMinor,
        Guid? fxRateMetadataId,
        Guid? vatCodeId,
        Guid? businessPartnerId,
        Guid? projectId,
        IReadOnlyList<LineAttribution> attributions)
    {
        AccountId = accountId;
        AmountMinor = amountMinor;
        Currency = currency;
        BaseAmountMinor = baseAmountMinor;
        FxRateMetadataId = fxRateMetadataId;
        VatCodeId = vatCodeId;
        BusinessPartnerId = businessPartnerId;
        ProjectId = projectId;
        Attributions = attributions;
    }

    public Guid AccountId { get; }

    public long AmountMinor { get; }

    public CurrencyCode Currency { get; }

    public long BaseAmountMinor { get; }

    public Guid? FxRateMetadataId { get; }

    public Guid? VatCodeId { get; }

    public Guid? BusinessPartnerId { get; }

    public Guid? ProjectId { get; }

    public IReadOnlyList<LineAttribution> Attributions { get; }

    public static Result<JournalLine> Create(
        Guid accountId,
        long amountMinor,
        CurrencyCode currency,
        long baseAmountMinor,
        Guid? fxRateMetadataId = null,
        Guid? vatCodeId = null,
        Guid? businessPartnerId = null,
        Guid? projectId = null,
        IReadOnlyCollection<LineAttribution>? attributions = null)
    {
        var attributionArray = attributions?.ToArray() ?? [];
        foreach (var attribution in attributionArray)
        {
            if (attribution.SharePermille is < 1 or > 1000)
            {
                return Result<JournalLine>.Failure(new DomainError(
                    "line_attribution.share_out_of_range",
                    "Attribution share must be between 1 and 1000 permille."));
            }
        }

        if (attributionArray.GroupBy(item => item.UserId).Any(group => group.Count() > 1))
        {
            return Result<JournalLine>.Failure(new DomainError(
                "line_attribution.duplicate_user",
                "A user may be attributed at most once per journal line."));
        }

        if (attributionArray.Length > 0 && attributionArray.Sum(item => (long)item.SharePermille) != 1000)
        {
            return Result<JournalLine>.Failure(new DomainError(
                "line_attribution.share_sum_invalid",
                "Attribution shares must sum to exactly 1000 permille."));
        }

        return Result<JournalLine>.Success(new JournalLine(
            accountId,
            amountMinor,
            currency,
            baseAmountMinor,
            fxRateMetadataId,
            vatCodeId,
            businessPartnerId,
            projectId,
            Array.AsReadOnly(attributionArray)));
    }

    internal Result<JournalLine> Negate()
    {
        if (AmountMinor == long.MinValue || BaseAmountMinor == long.MinValue)
        {
            return Result<JournalLine>.Failure(new DomainError(
                "journal_entry.amount_out_of_range",
                "A journal line containing the minimum 64-bit amount cannot be reversed."));
        }

        return Create(
            AccountId,
            -AmountMinor,
            Currency,
            -BaseAmountMinor,
            FxRateMetadataId,
            VatCodeId,
            BusinessPartnerId,
            ProjectId,
            Attributions);
    }
}
