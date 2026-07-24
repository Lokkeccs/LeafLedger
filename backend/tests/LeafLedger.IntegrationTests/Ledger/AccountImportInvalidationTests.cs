using LeafLedger.Modules.Ledger.Application.Accounts;
using LeafLedger.Modules.Ledger.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class AccountImportInvalidationTests
{
    private readonly LedgerDbFixture _fixture;

    public AccountImportInvalidationTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Only_committed_account_and_group_imports_enqueue_catalog_topics()
    {
        var space = await _fixture.SeedSpaceAsync();
        var groupSpace = await _fixture.SeedSpaceAsync();
        var actor = Guid.NewGuid();
        var queue = new RecordingInvalidationQueue();

        var accountRows = new[]
        {
            new CsvImportRow<AccountImportRow>(2, new AccountImportRow(
                "asset", 1102, "Imported Cash", "CHF", "Assets", true, null, null, null), []),
        };
        var accountKey = Ulid.NewUlid().ToString();
        AccountImportOutcome committedAccount;
        AccountImportOutcome replayedAccount;
        AccountImportOutcome collisionAccount;
        await using (var provider = CreateProvider(queue))
        await using (var scope = provider.CreateAsyncScope())
        {
            var accountImport = scope.ServiceProvider.GetRequiredService<IAccountImportService>();
            committedAccount = await accountImport.ImportAccountsAsync(space.SpaceId, actor, accountKey, accountRows);
            replayedAccount = await accountImport.ImportAccountsAsync(space.SpaceId, actor, accountKey, accountRows);
            collisionAccount = await accountImport.ImportAccountsAsync(space.SpaceId, actor, accountKey,
            [new CsvImportRow<AccountImportRow>(2, new AccountImportRow(
                "asset", 1101, "Different Import", "CHF", "Assets", true, null, null, null), [])]);
        }

        AccountImportOutcome rejectedGroup;
        AccountImportOutcome committedGroup;
        await using (var provider = CreateProvider(queue))
        await using (var scope = provider.CreateAsyncScope())
        {
            var accountImport = scope.ServiceProvider.GetRequiredService<IAccountImportService>();
            committedGroup = await accountImport.ImportGroupsAsync(groupSpace.SpaceId, actor, Ulid.NewUlid().ToString(),
            [new CsvImportRow<GroupImportRow>(2, new GroupImportRow(
                "Assets", 1000, 2000, null, null), [])]);
            rejectedGroup = await accountImport.ImportGroupsAsync(groupSpace.SpaceId, actor, Ulid.NewUlid().ToString(),
            [new CsvImportRow<GroupImportRow>(2, new GroupImportRow(
                "Overlapping", 1050, 1150, null, null), [])]);
        }

        Assert.True(committedAccount.IsSuccess);
        Assert.True(replayedAccount.IsReplay);
        Assert.False(collisionAccount.IsSuccess);
        Assert.False(rejectedGroup.IsSuccess);
        Assert.True(committedGroup.IsSuccess);
        Assert.Equal(2, queue.Enqueued.Count);
        Assert.All(queue.Enqueued, batch =>
        {
            Assert.Contains(batch.SpaceId, new[] { space.SpaceId, groupSpace.SpaceId });
            Assert.Equal(
                InvalidationTopics.AccountCatalogTopics.OrderBy(topic => topic),
                batch.Topics.OrderBy(topic => topic));
        });
    }

    private ServiceProvider CreateProvider(RecordingInvalidationQueue queue)
    {
        var services = new ServiceCollection()
            .AddLedgerModule(_fixture.ConnectionString);
        services.RemoveAll<ISpaceInvalidationQueue>();
        services.AddSingleton<ISpaceInvalidationQueue>(queue);
        return services.BuildServiceProvider();
    }

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