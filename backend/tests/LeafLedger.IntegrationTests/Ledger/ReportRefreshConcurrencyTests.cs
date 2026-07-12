using System.Net;
using LeafLedger.IntegrationTests.Ledger.FinancialProperties;
using LeafLedger.Modules.Ledger.Infrastructure;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class ReportRefreshConcurrencyTests
{
    private readonly LedgerDbFixture _fixture;

    public ReportRefreshConcurrencyTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Posting_and_concurrent_refresh_complete_without_lock_contention()
    {
        await using var driver = await LedgerSystemDriver.CreateAsync(_fixture);
        var post = new LedgerCommand.PostValid(
            new DateOnly(2026, 6, 30),
            driver.AccountId,
            driver.SecondAccountId,
            25,
            "00000000000000000000000000");

        var refresh = RefreshCoalescingService.RunRefreshPassAsync(_fixture.ConnectionString);
        var posting = driver.PostAsync(post);

        await Task.WhenAll(refresh, posting);

        Assert.Equal(HttpStatusCode.Created, (await posting).StatusCode);
        Assert.True(await refresh >= 0);
        Assert.Equal(0L, (await driver.GetTrialBalanceAsync()).Body.GetProperty("totalBaseBalanceMinor").GetInt64());
    }
}