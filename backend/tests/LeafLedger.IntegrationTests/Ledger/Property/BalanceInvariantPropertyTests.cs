using System.Net;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger.FinancialProperties;

[Trait("Category", "Property")]
[Collection("Ledger schema")]
public sealed class BalanceInvariantPropertyTests
{
    private readonly LedgerDbFixture _fixture;

    public BalanceInvariantPropertyTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Generated_valid_posts_and_retries_leave_a_zero_trial_balance()
    {
        await using var driver = await LedgerSystemDriver.CreateAsync(_fixture);
        var seed = 0x1A2B3C4DUL;
        Console.WriteLine($"WP08 I1 seed: {seed}");
        var entryIds = new Dictionary<string, Guid>();
        var model = new LedgerReferenceModel();

        for (var iteration = 0; iteration < LedgerCommandGenerator.Iterations; iteration++)
        {
            var sequence = LedgerCommandGenerator.GenerateSequence(
                new Random(unchecked((int)(seed + (ulong)iteration))),
                driver.AccountId,
                driver.SecondAccountId);
            foreach (var command in sequence)
            {
                switch (command)
                {
                    case LedgerCommand.PostValid post:
                    {
                        var response = await driver.PostAsync(post);
                        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                        entryIds.Add(post.IdempotencyKey, response.Body.GetProperty("id").GetGuid());
                        Assert.True(model.Apply(post));
                        break;
                    }
                    case LedgerCommand.Retry retry:
                    {
                        var response = await driver.PostAsync(retry.Original);
                        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                        Assert.True(response.IdempotentReplayed);
                        break;
                    }
                    case LedgerCommand.Reverse reverse:
                    {
                        var original = sequence.OfType<LedgerCommand.PostValid>().ElementAt(reverse.OriginalPostIndex);
                        var response = await driver.ReverseAsync(
                            entryIds[original.IdempotencyKey], reverse.Date, reverse.IdempotencyKey);
                        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                        Assert.True(model.ApplyReverse(original));
                        break;
                    }
                }

                Assert.Equal(0, model.TotalBalanceMinor);
                Assert.Equal(model.AccountBalances, await driver.GetTrialBalanceByAccountAsync());
                Assert.Equal(model.LiveEntryCount, await driver.GetLiveEntryCountAsync());
            }

            var trialBalance = await driver.GetTrialBalanceAsync();
            var integrity = await driver.GetIntegrityAsync();
            var secondIntegrity = await driver.GetIntegrityAsync();

            Assert.Equal(HttpStatusCode.OK, trialBalance.StatusCode);
            Assert.Equal(HttpStatusCode.OK, integrity.StatusCode);
            Assert.Equal(HttpStatusCode.OK, secondIntegrity.StatusCode);
            Assert.Equal(0, trialBalance.Body.GetProperty("totalBaseBalanceMinor").GetInt64());
            Assert.True(integrity.Body.GetProperty("balanced").GetBoolean());
            Assert.Equal(
                integrity.Body.GetProperty("trialBalanceHash").GetString(),
                secondIntegrity.Body.GetProperty("trialBalanceHash").GetString());
        }
    }

    [Fact]
    public async Task Generated_unbalanced_posts_are_rejected_without_state_change()
    {
        await using var driver = await LedgerSystemDriver.CreateAsync(_fixture);
        var seed = 0x5EED1234UL;
        Console.WriteLine($"WP08 I2 seed: {seed}");

        for (var iteration = 0; iteration < LedgerCommandGenerator.Iterations; iteration++)
        {
            var random = new Random(unchecked((int)(seed + (ulong)iteration)));
            var debit = random.Next(1, 1001);
            var credit = debit == 1000 ? debit - 1 : debit + 1;
            var command = new LedgerCommand.PostUnbalanced(
                new DateOnly(2026, 1, 1).AddDays(random.Next(0, 365)),
                driver.AccountId,
                debit,
                credit,
                PropertyRunner.DeterministicKey((ulong)iteration));
            var beforeEntryNo = await driver.GetMaxEntryNumberAsync();

            var response = await driver.PostUnbalancedAsync(command);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("journal_entry.unbalanced", response.Body.GetProperty("errors")[0].GetProperty("code").GetString());
            Assert.Equal(beforeEntryNo, await driver.GetMaxEntryNumberAsync());
            var trialBalance = await driver.GetTrialBalanceAsync();
            Assert.Equal(0, trialBalance.Body.GetProperty("totalBaseBalanceMinor").GetInt64());
        }
    }
}