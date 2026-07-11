using LeafLedger.Modules.ChartOfAccounts.Domain.Fx;
using LeafLedger.Modules.ChartOfAccounts.Domain.Groups;
using LeafLedger.SharedKernel;

namespace LeafLedger.Modules.ChartOfAccounts.Domain.Accounts;

public sealed class AccountTag : IEntityTag
{
    public static string Prefix => "acc";
}

public sealed class Account
{
    private Account(
        Id<AccountTag> id,
        Id<AccountGroupTag> groupId,
        int code,
        string name,
        CurrencyCode currency,
        AccountKind kind,
        bool isActive,
        DateOnly? validFrom,
        DateOnly? validTo,
        FxPolicyOverride? fxOverride)
    {
        Id = id;
        GroupId = groupId;
        Code = code;
        Name = name;
        Currency = currency;
        Kind = kind;
        IsActive = isActive;
        ValidFrom = validFrom;
        ValidTo = validTo;
        FxOverride = fxOverride;
    }

    public Id<AccountTag> Id { get; }

    public Id<AccountGroupTag> GroupId { get; }

    public int Code { get; }

    public string Name { get; }

    public CurrencyCode Currency { get; }

    public AccountKind Kind { get; }

    public bool IsActive { get; }

    public DateOnly? ValidFrom { get; }

    public DateOnly? ValidTo { get; }

    public FxPolicyOverride? FxOverride { get; }

    public static Result<Account> Create(
        Id<AccountTag> id,
        Id<AccountGroupTag> groupId,
        int code,
        string? name,
        string? currency,
        AccountKind kind,
        bool isActive,
        DateOnly? validFrom = null,
        DateOnly? validTo = null,
        FxPolicyOverride? fxOverride = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Account>.Failure(
                new DomainError("account.name_required", "Account name is required."));
        }

        var currencyResult = CurrencyCode.TryParse(currency?.Trim());
        if (currencyResult.IsFailure)
        {
            return Result<Account>.Failure(currencyResult.Error!);
        }

        if (validFrom.HasValue && validTo.HasValue && validFrom.Value > validTo.Value)
        {
            return Result<Account>.Failure(new DomainError(
                "account.validity_window_invalid",
                "Account valid-from date must not be after its valid-to date."));
        }

        return Result<Account>.Success(new Account(
            id,
            groupId,
            code,
            name.Trim(),
            currencyResult.Value,
            kind,
            isActive,
            validFrom,
            validTo,
            fxOverride));
    }
}
