using LeafLedger.Modules.ChartOfAccounts.Domain.Accounts;
using LeafLedger.Modules.ChartOfAccounts.Domain.Fx;
using LeafLedger.Modules.ChartOfAccounts.Domain.Groups;
using LeafLedger.SharedKernel;
using Xunit;

namespace LeafLedger.Modules.ChartOfAccounts.Tests;

public class AccountGroupTests
{
    [Fact]
    public void Valid_group_preserves_parent_range_and_fx_defaults()
    {
        var parentId = Id<AccountGroupTag>.New();
        var range = AccountCodeRange.Create(1000, 1099).Value;
        var defaults = new FxPolicyOverride(FxTreatment.Monetary, FxRateTiming.TransactionDate, true, null);

        var result = AccountGroup.Create(Id<AccountGroupTag>.New(), " Bank accounts ", range, parentId, defaults);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bank accounts", result.Value.Name);
        Assert.Equal(parentId, result.Value.ParentId);
        Assert.Equal(range, result.Value.CodeRange);
        Assert.Equal(defaults, result.Value.FxDefaults);
    }

    [Fact]
    public void Blank_name_is_rejected()
    {
        var result = AccountGroup.Create(
            Id<AccountGroupTag>.New(), " ", AccountCodeRange.Create(1000, 1099).Value);

        Assert.Equal("account_group.name_required", result.Error!.Code);
    }
}
