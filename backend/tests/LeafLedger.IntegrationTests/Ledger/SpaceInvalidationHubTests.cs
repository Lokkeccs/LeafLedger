using System.Collections.Concurrent;
using System.Text.Json;
using LeafLedger.IntegrationTests.Authorization;
using LeafLedger.Modules.Ledger.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class SpaceInvalidationHubTests
{
    private readonly LedgerDbFixture _fixture;

    public SpaceInvalidationHubTests(LedgerDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Authorized_member_receives_data_free_topic_payloads()
    {
        await using var factory = CreateFactory();
        var subject = Guid.NewGuid();
        var space = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(space, subject);
        var received = new ConcurrentBag<InvalidationPayload>();
        var receivedSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = CreateConnection(factory, space, subject);
        connection.On<JsonElement>("spaceInvalidated", payload =>
        {
            received.Add(new InvalidationPayload(
                payload.GetProperty("spaceId").GetGuid(),
                payload.GetProperty("topic").GetString()!));
            if (received.Count == InvalidationTopics.PostingTopics.Count)
            {
                receivedSignal.TrySetResult(true);
            }
        });

        await connection.StartAsync();
        var queue = factory.Services.GetRequiredService<ISpaceInvalidationQueue>();
        Assert.True(queue.TryEnqueue(space, InvalidationTopics.PostingTopics));
        await receivedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            InvalidationTopics.PostingTopics.OrderBy(topic => topic),
            received.Select(payload => payload.Topic).OrderBy(topic => topic));
        Assert.All(received, payload => Assert.Equal(space, payload.SpaceId));
    }

    [Fact]
    public async Task Non_member_connection_is_closed_before_group_join()
    {
        await using var factory = CreateFactory();
        var subject = Guid.NewGuid();
        var space = await _fixture.SeedBareSpaceAsync();
        var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = CreateConnection(factory, space, subject);
        connection.Closed += exception =>
        {
            closed.TrySetResult(exception);
            return Task.CompletedTask;
        };

        await connection.StartAsync();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(connection.State == HubConnectionState.Connected);
    }

    [Fact]
    public async Task Unauthenticated_connection_is_rejected()
    {
        await using var factory = CreateFactory();
        var space = await _fixture.SeedBareSpaceAsync();
        await using var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, $"hubs/space?spaceId={space:D}"),
                options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                })
            .Build();

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
    }

    [Fact]
    public async Task Fan_out_stays_within_the_authorized_space_group()
    {
        await using var factory = CreateFactory();
        var subject = Guid.NewGuid();
        var firstSpace = await _fixture.SeedBareSpaceAsync();
        var secondSpace = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(firstSpace, subject);
        await _fixture.SeedMembershipAsync(secondSpace, subject);
        var firstClientReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondClientReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var isolatedClientReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var firstClient = CreateConnection(factory, firstSpace, subject);
        await using var secondClient = CreateConnection(factory, firstSpace, subject);
        await using var isolatedClient = CreateConnection(factory, secondSpace, subject);
        RegisterSignal(firstClient, firstClientReceived);
        RegisterSignal(secondClient, secondClientReceived);
        RegisterSignal(isolatedClient, isolatedClientReceived);

        await firstClient.StartAsync();
        await secondClient.StartAsync();
        await isolatedClient.StartAsync();

        var queue = factory.Services.GetRequiredService<ISpaceInvalidationQueue>();
        Assert.True(queue.TryEnqueue(firstSpace, [InvalidationTopics.TrialBalance]));
        await Task.WhenAll(
            firstClientReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)),
            secondClientReceived.Task.WaitAsync(TimeSpan.FromSeconds(5)));

        var isolatedResult = await Task.WhenAny(
            isolatedClientReceived.Task,
            Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.NotSame(isolatedClientReceived.Task, isolatedResult);
    }

    [Fact]
    public async Task Rapid_duplicate_enqueues_emit_one_ping_per_topic_window()
    {
        await using var factory = CreateFactory();
        var subject = Guid.NewGuid();
        var space = await _fixture.SeedBareSpaceAsync();
        await _fixture.SeedMembershipAsync(space, subject);
        var received = 0;
        var firstPing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = CreateConnection(factory, space, subject);
        connection.On<JsonElement>("spaceInvalidated", payload =>
        {
            if (payload.GetProperty("topic").GetString() == InvalidationTopics.TrialBalance)
            {
                Interlocked.Increment(ref received);
                firstPing.TrySetResult(true);
            }
        });

        await connection.StartAsync();
        var queue = factory.Services.GetRequiredService<ISpaceInvalidationQueue>();
        for (var index = 0; index < 10; index++)
        {
            queue.TryEnqueue(space, [InvalidationTopics.TrialBalance]);
        }

        await firstPing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Equal(1, Volatile.Read(ref received));
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseTestServer();
                builder.UseEnvironment("Production");
                builder.UseSetting("ConnectionStrings:Postgres", _fixture.ConnectionString);
                builder.ConfigureTestServices(services =>
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
                        options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
                    }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.AuthenticationScheme,
                        _ => { });
                });
            });

    private static HubConnection CreateConnection(
        WebApplicationFactory<Program> factory,
        Guid spaceId,
        Guid subjectId) =>
        new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, $"hubs/space?spaceId={spaceId:D}"),
                options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    options.Headers["X-Test-Subject"] = subjectId.ToString();
                })
            .Build();

    private static void RegisterSignal(HubConnection connection, TaskCompletionSource<bool> signal) =>
        connection.On<JsonElement>("spaceInvalidated", _ => signal.TrySetResult(true));

    private sealed record InvalidationPayload(Guid SpaceId, string Topic);
}
