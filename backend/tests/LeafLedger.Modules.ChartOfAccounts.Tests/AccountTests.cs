using LeafLedger.Modules.ChartOfAccounts.Domain.Accounts;
using LeafLedger.Modules.ChartOfAccounts.Domain.Fx;
using LeafLedger.Modules.ChartOfAccounts.Domain.Groups;
using LeafLedger.SharedKernel;
using Xunit;

namespace LeafLedger.Modules.ChartOfAccounts.Tests;

public class AccountTests
{
    [Fact]
    public void Valid_account_preserves_structural_values_without_enforcing_group_range()
    {
        var fxOverride = new FxPolicyOverride(FxTreatment.Monetary, null, true, null);
        var result = Account.Create(
            Id<AccountTag>.New(),
            Id<AccountGroupTag>.New(),
            9999,
            " Swiss bank ",
            "chf",
            AccountKind.Asset,
            true,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            fxOverride);

        Assert.True(result.IsSuccess);
        Assert.Equal("Swiss bank", result.Value.Name);
        Assert.Equal(9999, result.Value.Code);
        Assert.Equal(CurrencyCode.Chf, result.Value.Currency);
        Assert.Equal(fxOverride, result.Value.FxOverride);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_name_is_rejected(string name)
    {
        var result = Account.Create(
            Id<AccountTag>.New(), Id<AccountGroupTag>.New(), 1000, name, "CHF", AccountKind.Asset, true);

        Assert.Equal("account.name_required", result.Error!.Code);
    }

    [Fact]
    public void Invalid_currency_is_rejected()
    {
        var result = Account.Create(
            Id<AccountTag>.New(), Id<AccountGroupTag>.New(), 1000, "Bank", "XYZ", AccountKind.Asset, true);

        Assert.Equal("currency.unsupported", result.Error!.Code);
    }

    [Fact]
    public void Reversed_validity_window_is_rejected()
    {
        var result = Account.Create(
            Id<AccountTag>.New(), Id<AccountGroupTag>.New(), 1000, "Bank", "CHF", AccountKind.Asset, true,
            new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 31));

        Assert.Equal("account.validity_window_invalid", result.Error!.Code);
    }

    [Fact]
    public void Public_model_exposes_no_floating_amount_type()
    {
        var forbidden = new[] { typeof(float), typeof(double), typeof(decimal) };
        var exposed = typeof(Account).GetProperties().Select(property => property.PropertyType);

        Assert.Empty(exposed.Intersect(forbidden));
    }
}
