using System.Net;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger.FinancialProperties;

[Trait("Category", "Property")]
[Collection("Ledger schema")]
public sealed class IdempotencyInvariantPropertyTests
{
    private readonly LedgerDbFixture _fixture;

    public IdempotencyInvariantPropertyTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Reusing_a_key_with_a_different_generated_payload_is_rejected()
    {
        await using var driver = await LedgerSystemDriver.CreateAsync(_fixture);
        var key = PropertyRunner.DeterministicKey(0x1D3A007UL);
        var first = new LedgerCommand.PostValid(
            new DateOnly(2026, 6, 30), driver.AccountId, driver.AccountId, 100, key);
        var changed = first with { AmountMinor = 101 };

        var original = await driver.PostAsync(first);
        var originalEntryNo = original.Body.GetProperty("entryNo").GetInt64();
        var collision = await driver.PostAsync(changed);

        Assert.Equal(HttpStatusCode.Created, original.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        Assert.Equal("idempotency.key_reused", collision.Body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(originalEntryNo, await driver.GetMaxEntryNumberAsync());
    }
}