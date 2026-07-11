using LeafLedger.Modules.ChartOfAccounts.Domain.Groups;
using Xunit;

namespace LeafLedger.Modules.ChartOfAccounts.Tests;

public class AccountCodeRangeTests
{
    [Fact]
    public void Contains_uses_inclusive_boundaries()
    {
        var range = AccountCodeRange.Create(1000, 1099).Value;

        Assert.True(range.Contains(1000));
        Assert.True(range.Contains(1099));
        Assert.False(range.Contains(999));
        Assert.False(range.Contains(1100));
    }

    [Fact]
    public void Start_after_end_is_rejected()
    {
        var result = AccountCodeRange.Create(1100, 1000);

        Assert.True(result.IsFailure);
        Assert.Equal("account_code_range.invalid", result.Error!.Code);
    }
}
