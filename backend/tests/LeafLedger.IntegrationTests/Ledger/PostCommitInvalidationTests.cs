using LeafLedger.Modules.Ledger.Application.Posting;
using LeafLedger.Modules.Ledger.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class PostCommitInvalidationTests
{
    private readonly LedgerDbFixture _fixture;

    public PostCommitInvalidationTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Only_committed_posts_enqueue_invalidation_topics()
    {
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedPeriodAsync(space.SpaceId, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        var queue = new RecordingInvalidationQueue();
        await using var provider = CreateProvider(queue);

        var rejected = await PostAsync(provider, CreateCommand(space, "Rejected", 100, -90));
        Assert.False(rejected.IsSuccess);

        var key = Ulid.NewUlid().ToString();
        var committed = await PostAsync(provider, CreateCommand(space, "Committed", 100, -100, key));
        Assert.True(committed.IsSuccess);

        var replay = await PostAsync(provider, CreateCommand(space, "Committed", 100, -100, key));
        Assert.True(replay.IsReplay);

        var collision = await PostAsync(provider, CreateCommand(space, "Collision", 200, -200, key));
        Assert.False(collision.IsSuccess);
        Assert.Equal(409, collision.Failure!.Status);

        Assert.Single(queue.Enqueued);
        Assert.Equal(space.SpaceId, queue.Enqueued[0].SpaceId);
        Assert.Equal(
            InvalidationTopics.PostingTopics.OrderBy(topic => topic),
            queue.Enqueued[0].Topics.OrderBy(topic => topic));
    }

    private ServiceProvider CreateProvider(RecordingInvalidationQueue queue)
    {
        var services = new ServiceCollection()
            .AddLedgerModule(_fixture.ConnectionString);
        services.RemoveAll<ISpaceInvalidationQueue>();
        services.AddSingleton<ISpaceInvalidationQueue>(queue);
        return services.BuildServiceProvider();
    }

    private static async Task<PostingOutcome> PostAsync(
        ServiceProvider provider,
        PostJournalEntryCommand command)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IJournalPostingService>()
            .PostAsync(command);
    }

    private static PostJournalEntryCommand CreateCommand(
        SeededSpace space,
        string description,
        long firstAmount,
        long secondAmount,
        string? idempotencyKey = null) =>
        new(
            space.SpaceId,
            Guid.NewGuid(),
            new DateOnly(2026, 6, 30),
            description,
            null,
            [
                new PostJournalLineRequest(space.AccountId, firstAmount, "CHF", firstAmount),
                new PostJournalLineRequest(space.AccountId, secondAmount, "CHF", secondAmount),
            ],
            IdempotencyKey: idempotencyKey);

    private sealed class RecordingInvalidationQueue : ISpaceInvalidationQueue
    {
        public List<SpaceInvalidationBatch> Enqueued { get; } = [];

        public bool TryEnqueue(Guid spaceId, IEnumerable<string> topics)
        {
            Enqueued.Add(new SpaceInvalidationBatch(spaceId, topics.ToArray()));
            return true;
        }

        public Task<IReadOnlyList<SpaceInvalidationBatch>> ReadBatchAsync(
            TimeSpan debounceWindow,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SpaceInvalidationBatch>>([]);
    }
}
