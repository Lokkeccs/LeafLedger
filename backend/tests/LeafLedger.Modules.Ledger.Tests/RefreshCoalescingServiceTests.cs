using System.Diagnostics.Metrics;
using LeafLedger.Modules.Ledger.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

public sealed class RefreshCoalescingServiceTests
{
    [Fact]
    public async Task Hosted_service_coalesces_a_burst_into_one_refresh_pass()
    {
        var queue = new ReportRefreshQueue();
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var metrics = new ReportingRefreshMetrics(services.GetRequiredService<IMeterFactory>());
        var refreshCount = 0;
        var refreshCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new RefreshCoalescingService(
            "unused",
            queue,
            metrics,
            TimeSpan.FromMilliseconds(20),
            _ =>
            {
                Interlocked.Increment(ref refreshCount);
                refreshCompleted.TrySetResult(true);
                return Task.FromResult(0L);
            });

        await service.StartAsync(CancellationToken.None);
        var space = Guid.NewGuid();
        Assert.True(queue.TryEnqueue(space));
        for (var index = 0; index < 20; index++)
        {
            Assert.False(queue.TryEnqueue(space));
        }

        await refreshCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, refreshCount);
    }
}