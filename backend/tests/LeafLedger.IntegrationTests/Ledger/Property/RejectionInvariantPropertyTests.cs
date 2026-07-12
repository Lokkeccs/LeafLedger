using System.Net;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger.FinancialProperties;

[Trait("Category", "Property")]
[Collection("Ledger schema")]
public sealed class RejectionInvariantPropertyTests
{
    private readonly LedgerDbFixture _fixture;

    public RejectionInvariantPropertyTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Undefined_period_posts_are_rejected_without_state_change()
    {
        await using var driver = await LedgerSystemDriver.CreateAsync(_fixture);
        var beforeEntryNo = await driver.GetMaxEntryNumberAsync();
        var command = new LedgerCommand.PostValid(
            new DateOnly(2027, 1, 1), driver.AccountId, driver.AccountId, 100, PropertyRunner.DeterministicKey(1));

        var response = await driver.PostAsync(command);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("posting_period.not_defined", response.Body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(beforeEntryNo, await driver.GetMaxEntryNumberAsync());
    }

    [Fact]
    public async Task Closed_period_posts_are_rejected_without_state_change()
    {
        await using var driver = await LedgerSystemDriver.CreateAsync(_fixture);
        await driver.CloseSeedPeriodAsync();
        var beforeEntryNo = await driver.GetMaxEntryNumberAsync();
        var command = new LedgerCommand.PostValid(
            new DateOnly(2026, 6, 30), driver.AccountId, driver.AccountId, 100, PropertyRunner.DeterministicKey(2));

        var response = await driver.PostAsync(command);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("posting_period.not_open", response.Body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(beforeEntryNo, await driver.GetMaxEntryNumberAsync());
    }

    [Fact]
    public async Task Reversing_a_missing_entry_is_rejected_without_state_change()
    {
        await using var driver = await LedgerSystemDriver.CreateAsync(_fixture);
        var beforeEntryNo = await driver.GetMaxEntryNumberAsync();

        var response = await driver.ReverseAsync(Guid.Empty, new DateOnly(2026, 6, 30), PropertyRunner.DeterministicKey(3));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("journal_entry.not_found", response.Body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(beforeEntryNo, await driver.GetMaxEntryNumberAsync());
    }

    [Fact]
    public async Task Locked_period_posts_are_rejected_without_state_change()
    {
        await using var driver = await LedgerSystemDriver.CreateAsync(_fixture);
        await driver.LockSeedPeriodAsync();
        var beforeEntryNo = await driver.GetMaxEntryNumberAsync();
        var response = await driver.PostAsync(new LedgerCommand.PostValid(
            new DateOnly(2026, 6, 30), driver.AccountId, driver.SecondAccountId, 100, "01ARZ3NDEKTSV4RRFFQ69G5FAV"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("posting_period.not_open", response.Body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(beforeEntryNo, await driver.GetMaxEntryNumberAsync());
    }

    [Fact]
    public async Task Invalid_and_cross_space_accounts_are_rejected()
    {
        await using var driver = await LedgerSystemDriver.CreateAsync(_fixture);
        var other = await _fixture.SeedSpaceAsync();
        await _fixture.SeedPeriodAsync(other.SpaceId, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        var missing = await driver.PostAsync(new LedgerCommand.PostValid(
            new DateOnly(2026, 6, 30), Guid.NewGuid(), driver.SecondAccountId, 100, "01ARZ3NDEKTSV4RRFFQ69G5FAW"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        Assert.Equal("posting_validity.missing", missing.Body.GetProperty("errors")[0].GetProperty("code").GetString());

        var crossSpace = await driver.PostAsync(new LedgerCommand.PostValid(
            new DateOnly(2026, 6, 30), other.AccountId, driver.SecondAccountId, 100, "01ARZ3NDEKTSV4RRFFQ69G5FAX"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, crossSpace.StatusCode);
    }

    [Fact]
    public async Task Already_reversed_entries_are_rejected()
    {
        await using var driver = await LedgerSystemDriver.CreateAsync(_fixture);
        var post = await driver.PostAsync(new LedgerCommand.PostValid(
            new DateOnly(2026, 6, 30), driver.AccountId, driver.SecondAccountId, 100, "01ARZ3NDEKTSV4RRFFQ69G5FAY"));
        var entryId = post.Body.GetProperty("id").GetGuid();
        var first = await driver.ReverseAsync(entryId, new DateOnly(2026, 6, 30), "01ARZ3NDEKTSV4RRFFQ69G5FAZ");
        var second = await driver.ReverseAsync(entryId, new DateOnly(2026, 6, 30), "01ARZ3NDEKTSV4RRFFQ69G5FB0");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal("journal_entry.already_reversed", second.Body.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Cross_space_reversal_targets_are_rejected_without_state_change()
    {
        await using var firstDriver = await LedgerSystemDriver.CreateAsync(_fixture);
        await using var secondDriver = await LedgerSystemDriver.CreateAsync(_fixture);

        var posted = await secondDriver.PostAsync(new LedgerCommand.PostValid(
            new DateOnly(2026, 6, 30), secondDriver.AccountId, secondDriver.SecondAccountId, 100,
            "01ARZ3NDEKTSV4RRFFQ69G5FB1"));
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        var entryId = posted.Body.GetProperty("id").GetGuid();
        var firstCount = await firstDriver.GetLiveEntryCountAsync();
        var secondCount = await secondDriver.GetLiveEntryCountAsync();

        var response = await firstDriver.ReverseAsync(
            entryId, new DateOnly(2026, 6, 30), "01ARZ3NDEKTSV4RRFFQ69G5FB2");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("journal_entry.not_found", response.Body.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(firstCount, await firstDriver.GetLiveEntryCountAsync());
        Assert.Equal(secondCount, await secondDriver.GetLiveEntryCountAsync());
    }
}