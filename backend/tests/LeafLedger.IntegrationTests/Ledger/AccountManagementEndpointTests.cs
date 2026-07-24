using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LeafLedger.IntegrationTests.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace LeafLedger.IntegrationTests.Ledger;

[Trait("Category", "Integration")]
[Collection("Ledger schema")]
public sealed class AccountManagementEndpointTests : IAsyncLifetime
{
    private static int _keySequence;
    private readonly LedgerDbFixture _fixture;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public AccountManagementEndpointTests(LedgerDbFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseTestServer();
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Postgres", _fixture.ConnectionString);
            builder.ConfigureTestServices(services => services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.AuthenticationScheme;
                    options.DefaultChallengeScheme = TestAuthHandler.AuthenticationScheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.AuthenticationScheme, _ => { }));
        });
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Owner_create_persists_account_and_writes_audit_row()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync(codeLow: 1000, codeHigh: 2000, accountCode: 1000);
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");

        var response = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/spaces/{space.SpaceId}/accounts/",
            new
            {
                groupId = space.GroupId,
                code = 1100,
                name = "Receivables",
                currency = "CHF",
                kind = "asset",
                isActive = true,
            },
            owner,
            NewKey());
        var body = await ReadJsonAsync(response);
        var accountId = body.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1100, body.GetProperty("code").GetInt32());
        Assert.Equal("Receivables", body.GetProperty("name").GetString());
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM accounts WHERE space_id = @space AND id = @id AND code = 1100;",
            ("space", space.SpaceId), ("id", accountId)));
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM audit_log WHERE table_name = 'accounts' AND row_id = @id AND action = 'INSERT';",
            ("id", accountId)));
    }

    [Fact]
    public async Task Viewer_can_export_accounts_as_canonical_csv()
    {
        var viewer = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, viewer, "Viewer");

        using var response = await SendGetAsync($"/api/v1/spaces/{space.SpaceId}/accounts/export", viewer, "ledger.write");
        var csv = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("kind,code,name,currency,group,isActive,validFrom,validTo,fxPolicy\r\n", csv, StringComparison.Ordinal);
        Assert.Contains("Cash", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owner_can_import_accounts_and_replay_the_same_file()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");
        const string csv = "kind,code,name,currency,group,isActive,validFrom,validTo,fxPolicy\r\n" +
            "asset,1100,Imported Cash,CHF,Assets,TRUE,,,\r\n";
        var key = NewKey();

        using var first = await SendCsvAsync($"/api/v1/spaces/{space.SpaceId}/accounts/import", csv, owner, key);
        using var replay = await SendCsvAsync($"/api/v1/spaces/{space.SpaceId}/accounts/import", csv, owner, key);
        var firstBody = await ReadJsonAsync(first);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(1, firstBody.GetProperty("created").GetInt32());
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM accounts WHERE space_id = @space AND code = 1100 AND name = 'Imported Cash';",
            ("space", space.SpaceId)));
    }

    [Fact]
    public async Task Owner_can_import_accounts_and_groups_from_json_rows_envelopes()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");

        using var groupImport = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/spaces/{space.SpaceId}/groups/import",
            new
            {
                rows = new[]
                {
                    new { name = "Liabilities", rangeStart = 3000, rangeEnd = 3099, parent = (string?)null, fxPolicy = (string?)null },
                },
            },
            owner,
            NewKey());
        using var accountImport = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/spaces/{space.SpaceId}/accounts/import",
            new
            {
                rows = new[]
                {
                    new { kind = "liability", code = 3001, name = "Imported Payables", currency = "CHF", group = "Liabilities", isActive = true, validFrom = (string?)null, validTo = (string?)null, fxPolicy = (string?)null },
                },
            },
            owner,
            NewKey());

        Assert.Equal(HttpStatusCode.OK, groupImport.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accountImport.StatusCode);
        Assert.Equal(1, (await ReadJsonAsync(groupImport)).GetProperty("created").GetInt32());
        Assert.Equal(1, (await ReadJsonAsync(accountImport)).GetProperty("created").GetInt32());
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM accounts WHERE space_id = @space AND code = 3001 AND name = 'Imported Payables';",
            ("space", space.SpaceId)));
    }

    [Fact]
    public async Task Invalid_account_row_rolls_back_the_entire_import()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");
        const string csv = "kind,code,name,currency,group,isActive,validFrom,validTo,fxPolicy\r\n" +
            "asset,1100,Should Roll Back,CHF,Assets,TRUE,,,\r\n" +
            "asset,1101,Unknown Group,CHF,Missing,TRUE,,,\r\n";

        using var response = await SendCsvAsync($"/api/v1/spaces/{space.SpaceId}/accounts/import", csv, owner, NewKey());
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(1, body.GetProperty("failed").GetInt32());
        Assert.Equal("account.group_unknown", body.GetProperty("rows")[1].GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(0L, await ScalarAsync(
            "SELECT count(*) FROM accounts WHERE space_id = @space AND code IN (1100, 1101);",
            ("space", space.SpaceId)));
    }

    [Fact]
    public async Task Groups_can_be_exported_and_imported_with_a_created_row_and_audit()
    {
        var owner = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");
        await _fixture.SeedMembershipAsync(space.SpaceId, viewer, "Viewer");

        using var export = await SendGetAsync($"/api/v1/spaces/{space.SpaceId}/groups/export", viewer, "ledger.write");
        var exported = await export.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.StartsWith("name,rangeStart,rangeEnd,parent,fxPolicy\r\n", exported, StringComparison.Ordinal);

        const string csv = "name,rangeStart,rangeEnd,parent,fxPolicy\r\n" +
            "Liabilities,3000,3099,,\r\n";
        using var imported = await SendCsvAsync($"/api/v1/spaces/{space.SpaceId}/groups/import", csv, owner, NewKey());
        var body = await ReadJsonAsync(imported);

        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        Assert.Equal(1, body.GetProperty("created").GetInt32());
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM account_groups WHERE space_id = @space AND name = 'Liabilities';",
            ("space", space.SpaceId)));
        Assert.True(await ScalarAsync(
            "SELECT count(*) FROM audit_log WHERE table_name = 'account_groups' AND action = 'INSERT' AND after->>'name' = 'Liabilities';") >= 1);
    }

    [Fact]
    public async Task Exported_account_csv_round_trips_as_an_update()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");

        using var export = await SendGetAsync($"/api/v1/spaces/{space.SpaceId}/accounts/export", owner, "ledger.write");
        var csv = await export.Content.ReadAsStringAsync();
        using var imported = await SendCsvAsync($"/api/v1/spaces/{space.SpaceId}/accounts/import", csv, owner, NewKey());
        var body = await ReadJsonAsync(imported);

        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        Assert.Equal(1, body.GetProperty("updated").GetInt32());
        Assert.Equal(0, body.GetProperty("failed").GetInt32());
        Assert.Equal("updated", body.GetProperty("rows")[0].GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Posted_account_immutable_import_rolls_back()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");
        await SeedPostedEntryAsync(space);
        const string csv = "kind,code,name,currency,group,isActive,validFrom,validTo,fxPolicy\r\n" +
            "asset,1000,Cash,EUR,Assets,TRUE,,,\r\n";

        using var response = await SendCsvAsync($"/api/v1/spaces/{space.SpaceId}/accounts/import", csv, owner, NewKey());
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("account.field_immutable_after_posting", body.GetProperty("rows")[0].GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM accounts WHERE space_id = @space AND code = 1000 AND currency = 'CHF';",
            ("space", space.SpaceId)));
    }

    [Fact]
    public async Task Deferred_columns_are_ignored_with_a_warning_and_audit_is_written()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");
        const string csv = "kind,code,name,currency,group,isActive,validFrom,validTo,fxPolicy,ownerEmail,cashFlowActivity\r\n" +
            "asset,1102,Warned Import,CHF,Assets,TRUE,,,,owner@example.test,operating\r\n";

        using var response = await SendCsvAsync($"/api/v1/spaces/{space.SpaceId}/accounts/import", csv, owner, NewKey());
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, body.GetProperty("rows")[0].GetProperty("warnings").GetArrayLength());
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM audit_log WHERE table_name = 'accounts' AND action = 'INSERT' AND after->>'name' = 'Warned Import';"));
    }

    [Fact]
    public async Task Import_same_key_with_a_different_file_returns_conflict()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");
        const string header = "kind,code,name,currency,group,isActive,validFrom,validTo,fxPolicy\r\n";
        var key = NewKey();

        using var first = await SendCsvAsync($"/api/v1/spaces/{space.SpaceId}/accounts/import", header + "asset,1103,First,CHF,Assets,TRUE,,,\r\n", owner, key);
        using var collision = await SendCsvAsync($"/api/v1/spaces/{space.SpaceId}/accounts/import", header + "asset,1104,Different,CHF,Assets,TRUE,,,\r\n", owner, key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        Assert.Equal("idempotency.key_reused", await FirstIssueCodeAsync(collision));
        Assert.Equal(0L, await ScalarAsync(
            "SELECT count(*) FROM accounts WHERE space_id = @space AND code = 1104;",
            ("space", space.SpaceId)));
    }

    [Fact]
    public async Task Imports_require_manage_permission_and_are_isolated_between_spaces()
    {
        var firstOwner = Guid.NewGuid();
        var firstMember = Guid.NewGuid();
        var firstViewer = Guid.NewGuid();
        var secondOwner = Guid.NewGuid();
        var first = await _fixture.SeedSpaceAsync();
        var second = await _fixture.SeedSpaceAsync(codeLow: 2000, codeHigh: 3000, accountCode: 2000);
        await _fixture.SeedMembershipAsync(first.SpaceId, firstOwner, "Owner");
        await _fixture.SeedMembershipAsync(first.SpaceId, firstMember, "Member");
        await _fixture.SeedMembershipAsync(first.SpaceId, firstViewer, "Viewer");
        await _fixture.SeedMembershipAsync(second.SpaceId, secondOwner, "Owner");

        const string firstCsv = "kind,code,name,currency,group,isActive,validFrom,validTo,fxPolicy\r\n" +
            "asset,1104,First Space Import,CHF,Assets,TRUE,,,\r\n";
        const string secondCsv = "kind,code,name,currency,group,isActive,validFrom,validTo,fxPolicy\r\n" +
            "asset,2104,Second Space Import,CHF,Assets,TRUE,,,\r\n";

        using var anonymous = await SendCsvAsync($"/api/v1/spaces/{first.SpaceId}/accounts/import", firstCsv, null, NewKey());
        using var member = await SendCsvAsync($"/api/v1/spaces/{first.SpaceId}/accounts/import", firstCsv, firstMember, NewKey());
        using var firstImport = await SendCsvAsync($"/api/v1/spaces/{first.SpaceId}/accounts/import", firstCsv, firstOwner, NewKey());
        using var secondImport = await SendCsvAsync($"/api/v1/spaces/{second.SpaceId}/accounts/import", secondCsv, secondOwner, NewKey());
        using var firstExport = await SendGetAsync($"/api/v1/spaces/{first.SpaceId}/accounts/export", firstViewer, "ledger.write");
        using var secondExport = await SendGetAsync($"/api/v1/spaces/{second.SpaceId}/accounts/export", secondOwner, "ledger.write");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, member.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstImport.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondImport.StatusCode);
        var firstCsvExport = await firstExport.Content.ReadAsStringAsync();
        var secondCsvExport = await secondExport.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Second Space Import", firstCsvExport, StringComparison.Ordinal);
        Assert.DoesNotContain("First Space Import", secondCsvExport, StringComparison.Ordinal);
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM accounts WHERE space_id = @space AND code = 1104 AND name = 'First Space Import';",
            ("space", first.SpaceId)));
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM accounts WHERE space_id = @space AND code = 2104 AND name = 'Second Space Import';",
            ("space", second.SpaceId)));
    }

    [Fact]
    public async Task Same_idempotency_key_replays_once_and_collision_returns_409()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");
        var payload = new
        {
            groupId = space.GroupId,
            code = 1100,
            name = "Receivables",
            currency = "CHF",
            kind = "asset",
            isActive = true,
        };
        var key = NewKey();

        using var first = await SendJsonAsync(HttpMethod.Post, $"/api/v1/spaces/{space.SpaceId}/accounts/", payload, owner, key);
        using var replay = await SendJsonAsync(HttpMethod.Post, $"/api/v1/spaces/{space.SpaceId}/accounts/", payload, owner, key);
        using var collision = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/spaces/{space.SpaceId}/accounts/",
            new
            {
                groupId = space.GroupId,
                code = 1101,
                name = "Different payload",
                currency = "CHF",
                kind = "asset",
                isActive = true,
            },
            owner,
            key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        Assert.Equal("idempotency.key_reused", await FirstIssueCodeAsync(collision));
        Assert.Equal(2L, await ScalarAsync(
            "SELECT count(*) FROM accounts WHERE space_id = @space;",
            ("space", space.SpaceId)));
    }

    [Fact]
    public async Task Authorization_and_rls_prevent_member_anonymous_and_foreign_writes_while_viewer_can_read_groups()
    {
        var owner = Guid.NewGuid();
        var member = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var first = await _fixture.SeedSpaceAsync();
        var second = await _fixture.SeedSpaceAsync(accountCode: 2000);
        await _fixture.SeedMembershipAsync(first.SpaceId, owner, "Owner");
        await _fixture.SeedMembershipAsync(first.SpaceId, member, "Member");
        await _fixture.SeedMembershipAsync(first.SpaceId, viewer, "Viewer");
        await _fixture.SeedMembershipAsync(second.SpaceId, viewer, "Viewer");

        using var memberResponse = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/spaces/{first.SpaceId}/accounts/",
            CreatePayload(first, 1100),
            member,
            NewKey());
        using var anonymousResponse = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/spaces/{first.SpaceId}/accounts/",
            CreatePayload(first, 1101),
            null,
            NewKey());
        using var groupsResponse = await SendGetAsync($"/api/v1/spaces/{first.SpaceId}/groups", viewer, "ledger.write");
        using var secondGroupsResponse = await SendGetAsync($"/api/v1/spaces/{second.SpaceId}/groups", viewer, "ledger.write");
        using var foreignPatch = await SendJsonAsync(
            HttpMethod.Patch,
            $"/api/v1/spaces/{first.SpaceId}/accounts/{second.AccountId}",
            new
            {
                groupId = first.GroupId,
                code = 1000,
                name = "Must not cross spaces",
                currency = "CHF",
                kind = "asset",
            },
            owner,
            NewKey());

        Assert.Equal(HttpStatusCode.Forbidden, memberResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, groupsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondGroupsResponse.StatusCode);
        var firstGroups = await ReadJsonAsync(groupsResponse);
        var secondGroups = await ReadJsonAsync(secondGroupsResponse);
        Assert.DoesNotContain(firstGroups.GetProperty("groups").EnumerateArray(), group => group.GetProperty("id").GetGuid() == second.GroupId);
        Assert.DoesNotContain(secondGroups.GetProperty("groups").EnumerateArray(), group => group.GetProperty("id").GetGuid() == first.GroupId);
        Assert.Equal(HttpStatusCode.NotFound, foreignPatch.StatusCode);
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM accounts WHERE space_id = @space AND id = @id AND name = 'Cash';",
            ("space", second.SpaceId), ("id", second.AccountId)));
    }

    [Fact]
    public async Task Duplicate_code_out_of_range_and_invalid_validity_return_structured_422s()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync(codeLow: 1000, codeHigh: 1100, accountCode: 1000);
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");

        using var duplicate = await SendJsonAsync(HttpMethod.Post, $"/api/v1/spaces/{space.SpaceId}/accounts/", CreatePayload(space, 1000), owner, NewKey());
        using var outsideRange = await SendJsonAsync(HttpMethod.Post, $"/api/v1/spaces/{space.SpaceId}/accounts/", CreatePayload(space, 1200), owner, NewKey());
        using var invalidValidity = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/spaces/{space.SpaceId}/accounts/",
            new
            {
                groupId = space.GroupId,
                code = 1050,
                name = "Invalid dates",
                currency = "CHF",
                kind = "asset",
                validFrom = "2026-12-31",
                validTo = "2026-01-01",
            },
            owner,
            NewKey());
        using var invalidCurrency = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/spaces/{space.SpaceId}/accounts/",
            new
            {
                groupId = space.GroupId,
                code = 1060,
                name = "Invalid currency",
                currency = "XYZ",
                kind = "asset",
                isActive = true,
            },
            owner,
            NewKey());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicate.StatusCode);
        Assert.Equal("account.code_taken", await FirstIssueCodeAsync(duplicate));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, outsideRange.StatusCode);
        Assert.Equal("account.code_out_of_group_range", await FirstIssueCodeAsync(outsideRange));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidValidity.StatusCode);
        Assert.Equal("account.validity_window_invalid", await FirstIssueCodeAsync(invalidValidity));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidCurrency.StatusCode);
        Assert.Equal("currency.unsupported", await FirstIssueCodeAsync(invalidCurrency));
    }

    [Fact]
    public async Task Deactivate_then_activate_changes_is_active()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");

        using var deactivate = await SendEmptyPostAsync($"/api/v1/spaces/{space.SpaceId}/accounts/{space.AccountId}/deactivate", owner, NewKey());
        using var activate = await SendEmptyPostAsync($"/api/v1/spaces/{space.SpaceId}/accounts/{space.AccountId}/activate", owner, NewKey());

        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.False((await ReadJsonAsync(deactivate)).GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        Assert.True((await ReadJsonAsync(activate)).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Deactivate_rejects_posting_and_reactivation_restores_posting()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedPeriodAsync(space.SpaceId, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1));
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");

        using var deactivate = await SendEmptyPostAsync($"/api/v1/spaces/{space.SpaceId}/accounts/{space.AccountId}/deactivate", owner, NewKey());
        using var rejectedPost = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/spaces/{space.SpaceId}/journal-entries",
            new
            {
                date = "2026-06-30",
                description = "Inactive account",
                lines = new[]
                {
                    new { accountId = space.AccountId, amountMinor = 100L, currency = "CHF", baseAmountMinor = 100L },
                    new { accountId = space.AccountId, amountMinor = -100L, currency = "CHF", baseAmountMinor = -100L },
                },
            },
            owner,
            NewKey());
        using var activate = await SendEmptyPostAsync($"/api/v1/spaces/{space.SpaceId}/accounts/{space.AccountId}/activate", owner, NewKey());
        using var acceptedPost = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/spaces/{space.SpaceId}/journal-entries",
            new
            {
                date = "2026-06-30",
                description = "Reactivated account",
                lines = new[]
                {
                    new { accountId = space.AccountId, amountMinor = 100L, currency = "CHF", baseAmountMinor = 100L },
                    new { accountId = space.AccountId, amountMinor = -100L, currency = "CHF", baseAmountMinor = -100L },
                },
            },
            owner,
            NewKey());

        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejectedPost.StatusCode);
        Assert.Equal("posting_validity.inactive", await FirstIssueCodeAsync(rejectedPost));
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, acceptedPost.StatusCode);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("currency")]
    [InlineData("kind")]
    [InlineData("validFrom")]
    [InlineData("validTo")]
    [InlineData("fxPolicy")]
    [InlineData("groupId")]
    public async Task Posted_account_rejects_each_immutable_update(string field)
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");
        await SeedPostedEntryAsync(space);

        using var immutable = await SendJsonAsync(
            HttpMethod.Patch,
            $"/api/v1/spaces/{space.SpaceId}/accounts/{space.AccountId}",
            ImmutableUpdatePayload(space, field),
            owner,
            NewKey());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, immutable.StatusCode);
        Assert.Equal("account.field_immutable_after_posting", await FirstIssueCodeAsync(immutable));
    }

    [Fact]
    public async Task Posted_account_allows_name_update_and_audits_it()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync();
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");
        await SeedPostedEntryAsync(space);

        using var rename = await SendJsonAsync(
            HttpMethod.Patch,
            $"/api/v1/spaces/{space.SpaceId}/accounts/{space.AccountId}",
            new
            {
                groupId = space.GroupId,
                code = 1000,
                name = "Renamed Cash",
                currency = "CHF",
                kind = "asset",
            },
            owner,
            NewKey());

        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal("Renamed Cash", (await ReadJsonAsync(rename)).GetProperty("name").GetString());
        Assert.Equal(1L, await ScalarAsync(
            "SELECT count(*) FROM audit_log WHERE table_name = 'accounts' AND row_id = @id AND action = 'UPDATE';",
            ("id", space.AccountId)));
    }

    private static object ImmutableUpdatePayload(SeededSpace space, string field) => field switch
    {
        "code" => new { groupId = space.GroupId, code = 1001, name = "Cash", currency = "CHF", kind = "asset" },
        "currency" => new { groupId = space.GroupId, code = 1000, name = "Cash", currency = "EUR", kind = "asset" },
        "kind" => new { groupId = space.GroupId, code = 1000, name = "Cash", currency = "CHF", kind = "liability" },
        "validFrom" => new { groupId = space.GroupId, code = 1000, name = "Cash", currency = "CHF", kind = "asset", validFrom = "2026-02-01" },
        "validTo" => new { groupId = space.GroupId, code = 1000, name = "Cash", currency = "CHF", kind = "asset", validTo = "2026-12-31" },
        "fxPolicy" => new { groupId = space.GroupId, code = 1000, name = "Cash", currency = "CHF", kind = "asset", fxPolicy = "monetary" },
        "groupId" => new { groupId = Guid.NewGuid(), code = 1000, name = "Cash", currency = "CHF", kind = "asset" },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    [Fact]
    public async Task Group_create_overlap_and_member_excluding_range_return_structured_422s()
    {
        var owner = Guid.NewGuid();
        var space = await _fixture.SeedSpaceAsync(codeLow: 1000, codeHigh: 2000, accountCode: 1000);
        await _fixture.SeedMembershipAsync(space.SpaceId, owner, "Owner");

        using var create = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/spaces/{space.SpaceId}/groups/",
            new { name = "Liabilities", rangeStart = 3000, rangeEnd = 3100 },
            owner,
            NewKey());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var overlap = await SendJsonAsync(
            HttpMethod.Post,
            $"/api/v1/spaces/{space.SpaceId}/groups/",
            new { name = "Overlap", rangeStart = 3050, rangeEnd = 3150 },
            owner,
            NewKey());
        using var excludesMember = await SendJsonAsync(
            HttpMethod.Patch,
            $"/api/v1/spaces/{space.SpaceId}/groups/{space.GroupId}",
            new { name = "Assets", rangeStart = 1100, rangeEnd = 2000 },
            owner,
            NewKey());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, overlap.StatusCode);
        Assert.Equal("group.code_range_overlap", await FirstIssueCodeAsync(overlap));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, excludesMember.StatusCode);
        Assert.Equal("group.range_excludes_member", await FirstIssueCodeAsync(excludesMember));
    }

    private static object CreatePayload(SeededSpace space, int code) => new
    {
        groupId = space.GroupId,
        code,
        name = $"Account {code}",
        currency = "CHF",
        kind = "asset",
        isActive = true,
    };

    private async Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string path, object payload, Guid? subject, string key)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(payload),
        };
        AddAuth(request, subject, "ledger.write");
        request.Headers.Add("Idempotency-Key", key);
        return await _client!.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendCsvAsync(string path, string csv, Guid? subject, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(csv, Encoding.UTF8, "text/csv"),
        };
        AddAuth(request, subject, "ledger.write");
        request.Headers.Add("Idempotency-Key", key);
        return await _client!.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendEmptyPostAsync(string path, Guid subject, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        AddAuth(request, subject, "ledger.write");
        request.Headers.Add("Idempotency-Key", key);
        return await _client!.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendGetAsync(string path, Guid? subject, string? scope)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        AddAuth(request, subject, scope);
        return await _client!.SendAsync(request);
    }

    private static void AddAuth(HttpRequestMessage request, Guid? subject, string? scope)
    {
        if (subject is Guid authenticatedSubject)
        {
            request.Headers.Add("X-Test-Subject", authenticatedSubject.ToString());
        }

        if (scope is not null)
        {
            request.Headers.Add("X-Test-Scope", scope);
        }
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> FirstIssueCodeAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.GetProperty("errors")[0].GetProperty("code").GetString()!;
    }

    private async Task<long> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task SeedPostedEntryAsync(SeededSpace space)
    {
        var balancingAccountId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        await using var connection = await _fixture.OpenSuperuserAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO accounts (id, space_id, group_id, code, name, currency, kind, is_active, created_at) " +
            "VALUES (@balancing, @space, @group, 1001, 'Equity', 'CHF', 'equity', true, now()); " +
            "INSERT INTO journal_entries (id, space_id, entry_no, date, status, created_by, created_at) " +
            "VALUES (@entry, @space, 1, DATE '2026-01-01', 'posted', @actor, now()); " +
            "INSERT INTO journal_lines (id, entry_id, space_id, account_id, amount_minor, currency, base_amount_minor) " +
            "VALUES (@line1, @entry, @space, @account, 100, 'CHF', 100), " +
            "(@line2, @entry, @space, @balancing, -100, 'CHF', -100);",
            connection);
        command.Parameters.AddWithValue("balancing", balancingAccountId);
        command.Parameters.AddWithValue("space", space.SpaceId);
        command.Parameters.AddWithValue("group", space.GroupId);
        command.Parameters.AddWithValue("entry", entryId);
        command.Parameters.AddWithValue("actor", Guid.NewGuid());
        command.Parameters.AddWithValue("line1", Guid.NewGuid());
        command.Parameters.AddWithValue("line2", Guid.NewGuid());
        command.Parameters.AddWithValue("account", space.AccountId);
        await command.ExecuteNonQueryAsync();
    }

    private static string NewKey() => $"01J000000000000000000000{Interlocked.Increment(ref _keySequence):00}";
}
