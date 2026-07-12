using System.Net;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger.FinancialProperties;

[Trait("Category", "Property")]
[Collection("Ledger schema")]
public sealed class PeriodBoundaryPropertyTests
{
    private readonly LedgerDbFixture _fixture;

    public PeriodBoundaryPropertyTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Half_open_period_accepts_last_included_date_and_rejects_first_excluded_date()
    {
        const ulong seed = 0x71B0DA7AUL;
        Console.WriteLine($"WP08 I7 seed: {seed}");

        await PropertyRunner.RunAsync(
            propertyName: "half-open period boundary",
            iterations: LedgerCommandGenerator.Iterations,
            seed: seed,
            generate: random => random.Next(1, 1001),
            succeeds: async amount =>
            {
                await using var driver = await LedgerSystemDriver.CreateAsync(_fixture);
                var accepted = await driver.PostAsync(new LedgerCommand.PostValid(
                    new DateOnly(2026, 12, 31), driver.AccountId, driver.SecondAccountId, amount,
                    PropertyRunner.DeterministicKey((ulong)amount + 1000)));
                var rejected = await driver.PostAsync(new LedgerCommand.PostValid(
                    new DateOnly(2027, 1, 1), driver.AccountId, driver.SecondAccountId, amount,
                    PropertyRunner.DeterministicKey((ulong)amount + 2000)));

                return accepted.StatusCode == HttpStatusCode.Created
                    && rejected.StatusCode == HttpStatusCode.UnprocessableEntity
                    && rejected.Body.GetProperty("errors")[0].GetProperty("code").GetString()
                        == "posting_period.not_defined";
            },
            shrink: amount => amount == 1 ? [] : [amount / 2]);
    }
}