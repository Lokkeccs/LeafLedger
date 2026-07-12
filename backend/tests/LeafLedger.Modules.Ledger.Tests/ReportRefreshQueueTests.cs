using LeafLedger.Modules.Ledger.Infrastructure;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public sealed class ReportRefreshQueueTests
{
    [Fact]
    public async Task Duplicate_space_requests_are_coalesced_into_one_batch_item()
    {
        var queue = new ReportRefreshQueue();
        var space = Guid.NewGuid();

        Assert.True(queue.TryEnqueue(space));
        Assert.False(queue.TryEnqueue(space));

        var batch = await queue.ReadBatchAsync(TimeSpan.FromMilliseconds(1), CancellationToken.None);

        Assert.Single(batch.SpaceIds);
        Assert.Equal(space, batch.SpaceIds.Single());
    }

    [Fact]
    public async Task Requests_for_multiple_spaces_share_one_refresh_batch()
    {
        var queue = new ReportRefreshQueue();
        var spaces = new[] { Guid.NewGuid(), Guid.NewGuid() };

        foreach (var space in spaces)
        {
            Assert.True(queue.TryEnqueue(space));
        }

        var batch = await queue.ReadBatchAsync(TimeSpan.FromMilliseconds(1), CancellationToken.None);

        Assert.Equal(spaces.OrderBy(item => item), batch.SpaceIds.OrderBy(item => item));
    }
}