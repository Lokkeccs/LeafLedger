using LeafLedger.Modules.Ledger.Infrastructure;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

public sealed class SpaceInvalidationQueueTests
{
    [Fact]
    public async Task Coalesces_duplicate_space_topics_into_one_batch()
    {
        var queue = new SpaceInvalidationQueue();
        var spaceId = Guid.NewGuid();

        Assert.True(queue.TryEnqueue(spaceId, new[] { InvalidationTopics.TrialBalance, InvalidationTopics.JournalEntries }));
        Assert.False(queue.TryEnqueue(spaceId, new[] { InvalidationTopics.TrialBalance, InvalidationTopics.JournalEntries }));

        var batch = await queue.ReadBatchAsync(TimeSpan.Zero);

        Assert.Single(batch);
        Assert.Equal(spaceId, batch[0].SpaceId);
        Assert.Equal(
            new[] { InvalidationTopics.TrialBalance, InvalidationTopics.JournalEntries },
            batch[0].Topics);
    }
}
