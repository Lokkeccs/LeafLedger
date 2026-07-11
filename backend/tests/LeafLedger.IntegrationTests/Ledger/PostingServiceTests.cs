using LeafLedger.Modules.Ledger.Application.Posting;
using LeafLedger.Modules.Ledger.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class PostingServiceTests
{
    private readonly LedgerDbFixture _fixture;

    public PostingServiceTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Balanced_post_is_persisted_with_entry_number_under_rls()
    {
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedPeriodAsync(space.SpaceId, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        var actor = Guid.NewGuid();

        var outcome = await PostAsync(new PostJournalEntryCommand(
            space.SpaceId,
            actor,
            new DateOnly(2026, 6, 30),
            "Test posting",
            null,
            [
                new PostJournalLineRequest(space.AccountId, 100, "CHF", 100),
                new PostJournalLineRequest(space.AccountId, -100, "CHF", -100),
            ]));

        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, outcome.Value!.EntryNo);

        await using var connection = await _fixture.OpenAppAsync(space.SpaceId);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM journal_entries WHERE space_id = @space;", connection);
        command.Parameters.AddWithValue("space", space.SpaceId);
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Unbalanced_post_returns_domain_error_without_persisting_rows()
    {
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedPeriodAsync(space.SpaceId, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));

        var outcome = await PostAsync(new PostJournalEntryCommand(
            space.SpaceId,
            Guid.NewGuid(),
            new DateOnly(2026, 6, 30),
            "Unbalanced posting",
            null,
            [
                new PostJournalLineRequest(space.AccountId, 100, "CHF", 100),
                new PostJournalLineRequest(space.AccountId, -90, "CHF", -90),
            ]));

        Assert.False(outcome.IsSuccess);
        Assert.Equal("journal_entry.unbalanced", Assert.Single(outcome.Failure!.Issues).Code);

        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM journal_entries WHERE space_id = @space;", connection);
        command.Parameters.AddWithValue("space", space.SpaceId);
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Period_end_exclusive_boundary_is_not_postable()
    {
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedPeriodAsync(space.SpaceId, new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1));

        var outcome = await PostAsync(new PostJournalEntryCommand(
            space.SpaceId,
            Guid.NewGuid(),
            new DateOnly(2026, 7, 1),
            "Boundary posting",
            null,
            [
                new PostJournalLineRequest(space.AccountId, 100, "CHF", 100),
                new PostJournalLineRequest(space.AccountId, -100, "CHF", -100),
            ]));

        Assert.False(outcome.IsSuccess);
        Assert.Equal("posting_period.not_defined", Assert.Single(outcome.Failure!.Issues).Code);
    }

    [Fact]
    public async Task Closed_period_is_rejected_and_reversal_appends_negated_lines()
    {
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedPeriodAsync(space.SpaceId, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        var post = await PostAsync(new PostJournalEntryCommand(
            space.SpaceId,
            Guid.NewGuid(),
            new DateOnly(2026, 6, 30),
            "Reversible posting",
            null,
            [
                new PostJournalLineRequest(space.AccountId, 100, "CHF", 100),
                new PostJournalLineRequest(space.AccountId, -100, "CHF", -100),
            ]));
        Assert.True(post.IsSuccess);

        var reversal = await ReverseAsync(new ReverseJournalEntryCommand(
            space.SpaceId, Guid.NewGuid(), post.Value!.Id, new DateOnly(2026, 7, 1)));
        Assert.True(reversal.IsSuccess);
        Assert.Equal(post.Value.Id, reversal.Value!.ReversesEntryId);

        await using var connection = await _fixture.OpenAppAsync(space.SpaceId);
        await using var command = new NpgsqlCommand(
            "SELECT amount_minor FROM journal_lines WHERE entry_id = @entry ORDER BY amount_minor;", connection);
        command.Parameters.AddWithValue("entry", reversal.Value.Id);
        await using var reader = await command.ExecuteReaderAsync();
        var amounts = new List<long>();
        while (await reader.ReadAsync())
        {
            amounts.Add(reader.GetInt64(0));
        }

        Assert.Equal([-100L, 100L], amounts);

        await using var closedConnection = await _fixture.OpenSuperuserAsync();
        await using var closeCommand = new NpgsqlCommand(
            "UPDATE periods SET state = 'closed' WHERE space_id = @space;", closedConnection);
        closeCommand.Parameters.AddWithValue("space", space.SpaceId);
        await closeCommand.ExecuteNonQueryAsync();
        var rejected = await PostAsync(new PostJournalEntryCommand(
            space.SpaceId, Guid.NewGuid(), new DateOnly(2026, 8, 1), "Closed", null,
            [
                new PostJournalLineRequest(space.AccountId, 1, "CHF", 1),
                new PostJournalLineRequest(space.AccountId, -1, "CHF", -1),
            ]));
        Assert.Equal("posting_period.not_open", Assert.Single(rejected.Failure!.Issues).Code);
    }

    [Fact]
    public async Task Reversal_preserves_line_attributions_through_postgres()
    {
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedPeriodAsync(space.SpaceId, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        var post = await PostAsync(new PostJournalEntryCommand(
            space.SpaceId,
            Guid.NewGuid(),
            new DateOnly(2026, 6, 30),
            "Attributed posting",
            null,
            [
                new PostJournalLineRequest(space.AccountId, 100, "CHF", 100, Attributions: [
                    new LineAttributionRequest(firstUser, 600),
                    new LineAttributionRequest(secondUser, 400),
                ]),
                new PostJournalLineRequest(space.AccountId, -100, "CHF", -100),
            ]));
        Assert.True(post.IsSuccess);

        var reversal = await ReverseAsync(new ReverseJournalEntryCommand(
            space.SpaceId, Guid.NewGuid(), post.Value!.Id, new DateOnly(2026, 7, 1)));
        Assert.True(reversal.IsSuccess);

        await using var connection = await _fixture.OpenAppAsync(space.SpaceId);
        await using var command = new NpgsqlCommand(
            "SELECT user_id, share_permille FROM line_attributions " +
            "WHERE line_id IN (SELECT id FROM journal_lines WHERE entry_id = @entry) " +
            "ORDER BY user_id;", connection);
        command.Parameters.AddWithValue("entry", reversal.Value!.Id);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(Guid UserId, int SharePermille)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetGuid(0), reader.GetInt32(1)));
        }

        Assert.Equal(2, rows.Count);
        Assert.Contains((firstUser, 600), rows);
        Assert.Contains((secondUser, 400), rows);
    }

    [Fact]
    public async Task Concurrent_posts_receive_distinct_entry_numbers()
    {
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedPeriodAsync(space.SpaceId, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        var posts = Enumerable.Range(0, 4).Select(index => PostAsync(new PostJournalEntryCommand(
            space.SpaceId,
            Guid.NewGuid(),
            new DateOnly(2026, 6, 30),
            $"Concurrent {index}",
            null,
            [
                new PostJournalLineRequest(space.AccountId, 100, "CHF", 100),
                new PostJournalLineRequest(space.AccountId, -100, "CHF", -100),
            ]))).ToArray();

        var outcomes = await Task.WhenAll(posts);
        Assert.All(outcomes, outcome => Assert.True(outcome.IsSuccess));
        Assert.Equal([1L, 2L, 3L, 4L], outcomes.Select(outcome => outcome.Value!.EntryNo).OrderBy(value => value));
    }

    private async Task<PostingOutcome> PostAsync(PostJournalEntryCommand command)
    {
        await using var provider = new ServiceCollection()
            .AddLedgerModule(_fixture.ConnectionString)
            .BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IJournalPostingService>();
        return await service.PostAsync(command);
    }

    private async Task<PostingOutcome> ReverseAsync(ReverseJournalEntryCommand command)
    {
        await using var provider = new ServiceCollection()
            .AddLedgerModule(_fixture.ConnectionString)
            .BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IJournalPostingService>();
        return await service.ReverseAsync(command);
    }
}