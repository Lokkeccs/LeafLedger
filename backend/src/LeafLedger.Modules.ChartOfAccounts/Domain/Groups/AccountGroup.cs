using LeafLedger.Modules.ChartOfAccounts.Domain.Fx;
using LeafLedger.SharedKernel;

namespace LeafLedger.Modules.ChartOfAccounts.Domain.Groups;

public sealed class AccountGroupTag : IEntityTag
{
    public static string Prefix => "ag";
}

public sealed class AccountGroup
{
    private AccountGroup(
        Id<AccountGroupTag> id,
        string name,
        AccountCodeRange codeRange,
        Id<AccountGroupTag>? parentId,
        FxPolicyOverride? fxDefaults)
    {
        Id = id;
        Name = name;
        CodeRange = codeRange;
        ParentId = parentId;
        FxDefaults = fxDefaults;
    }

    public Id<AccountGroupTag> Id { get; }

    public string Name { get; }

    public AccountCodeRange CodeRange { get; }

    public Id<AccountGroupTag>? ParentId { get; }

    public FxPolicyOverride? FxDefaults { get; }

    public static Result<AccountGroup> Create(
        Id<AccountGroupTag> id,
        string? name,
        AccountCodeRange codeRange,
        Id<AccountGroupTag>? parentId = null,
        FxPolicyOverride? fxDefaults = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<AccountGroup>.Failure(new DomainError(
                "account_group.name_required",
                "Account-group name is required."));
        }

        return Result<AccountGroup>.Success(new AccountGroup(
            id,
            name.Trim(),
            codeRange,
            parentId,
            fxDefaults));
    }
}
