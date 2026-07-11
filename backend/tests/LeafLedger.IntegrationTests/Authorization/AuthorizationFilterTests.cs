using System.Security.Claims;
using System.Text.Json;
using LeafLedger.Host.Authorization;
using LeafLedger.Modules.Ledger.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LeafLedger.IntegrationTests.Authorization;

public sealed class AuthorizationFilterTests
{
    private static readonly Guid SubjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SpaceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Unauthenticated_request_returns_401_before_license_or_membership()
    {
        var license = new RecordingLicenseEntitlement(true);
        var membership = new RecordingMembershipQuery("Member");
        var result = await InvokeAsync(new FakeCurrentUser(false, SubjectId, "ledger.write"), license, membership);

        var problem = await ReadProblemAsync(result.Result, result.Context);
        Assert.Equal(401, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("auth.unauthenticated", problem.RootElement.GetProperty("code").GetString());
        Assert.False(license.Called);
        Assert.False(membership.Called);
    }

    [Fact]
    public async Task Missing_scope_returns_403_before_license_or_membership()
    {
        var license = new RecordingLicenseEntitlement(true);
        var membership = new RecordingMembershipQuery("Member");
        var result = await InvokeAsync(new FakeCurrentUser(true, SubjectId), license, membership);

        var problem = await ReadProblemAsync(result.Result, result.Context);
        Assert.Equal(403, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("auth.forbidden", problem.RootElement.GetProperty("code").GetString());
        Assert.False(license.Called);
        Assert.False(membership.Called);
    }

    [Fact]
    public async Task License_denial_returns_403_before_membership_lookup()
    {
        var license = new RecordingLicenseEntitlement(false);
        var membership = new RecordingMembershipQuery("Member");
        var result = await InvokeAsync(new FakeCurrentUser(true, SubjectId, "ledger.write"), license, membership);

        var problem = await ReadProblemAsync(result.Result, result.Context);
        Assert.Equal(403, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("auth.license_inactive", problem.RootElement.GetProperty("code").GetString());
        Assert.True(license.Called);
        Assert.False(membership.Called);
    }

    [Fact]
    public async Task Viewer_is_denied_and_authorized_member_reaches_endpoint()
    {
        var viewerMembership = new RecordingMembershipQuery("Viewer");
        var viewer = await InvokeAsync(new FakeCurrentUser(true, SubjectId, "ledger.write"), new RecordingLicenseEntitlement(true), viewerMembership);
        var viewerProblem = await ReadProblemAsync(viewer.Result, viewer.Context);
        Assert.Equal("auth.permission_denied", viewerProblem.RootElement.GetProperty("code").GetString());
        Assert.True(viewerMembership.Called);

        var memberMembership = new RecordingMembershipQuery("Member");
        var member = await InvokeAsync(new FakeCurrentUser(true, SubjectId, "ledger.write"), new RecordingLicenseEntitlement(true), memberMembership);
        Assert.Equal("next", member.Result);
        Assert.True(memberMembership.Called);
    }

    private static async Task<(object? Result, DefaultHttpContext Context)> InvokeAsync(
        ICurrentUser currentUser,
        ILicenseEntitlement license,
        ISpaceMembershipQuery membership)
    {
        var context = new DefaultHttpContext();
        context.Request.RouteValues["spaceId"] = SpaceId;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", SubjectId.ToString()), new Claim("scope", "ledger.write")],
            "test"));
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddSingleton(currentUser)
            .AddSingleton(license)
            .AddSingleton(membership)
            .BuildServiceProvider();

        var filter = new RequireSpacePermissionFilter(ModulePermissions.PostLedger, "ledger.write");
        var result = await filter.InvokeAsync(
            new TestInvocationContext(context),
            _ => new ValueTask<object?>("next"));
        return (result, context);
    }

    private static async Task<JsonDocument> ReadProblemAsync(object? result, DefaultHttpContext context)
    {
        var problem = Assert.IsAssignableFrom<IResult>(result);
        context.Response.Body = new MemoryStream();
        await problem.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }

    private sealed class TestInvocationContext(DefaultHttpContext context) : EndpointFilterInvocationContext
    {
        public override HttpContext HttpContext => context;

        public override IList<object?> Arguments { get; } = [];

        public override T GetArgument<T>(int index) => (T)Arguments[index]!;
    }

    private sealed class FakeCurrentUser(bool authenticated, Guid? subjectId, params string[] scopes) : ICurrentUser
    {
        public bool IsAuthenticated { get; } = authenticated;

        public Guid? SubjectId { get; } = subjectId;

        public IReadOnlySet<string> Scopes { get; } = scopes.ToHashSet(StringComparer.Ordinal);

        public bool HasScope(string scope) => Scopes.Contains(scope);
    }

    private sealed class RecordingLicenseEntitlement(bool entitled) : ILicenseEntitlement
    {
        public bool Called { get; private set; }

        public Task<bool> IsEntitledAsync(Guid subjectId, Guid spaceId, string permission, CancellationToken cancellationToken = default)
        {
            Called = true;
            return Task.FromResult(entitled);
        }
    }

    private sealed class RecordingMembershipQuery(string? role) : ISpaceMembershipQuery
    {
        public bool Called { get; private set; }

        public Task<string?> GetRoleAsync(Guid spaceId, Guid userId, CancellationToken cancellationToken = default)
        {
            Called = true;
            return Task.FromResult(role);
        }
    }
}