using LeafLedger.Modules.Ledger.Application.Posting;
using LeafLedger.Modules.Ledger.Application.Reporting;
using LeafLedger.Modules.Ledger.Application.Accounts;
using System.Text;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LeafLedger.Modules.Ledger.Infrastructure;

public static class LedgerEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapLedgerEndpoints(
        this IEndpointRouteBuilder endpoints,
        Action<RouteHandlerBuilder, string>? configureAuthorization = null)
    {
        var group = endpoints.MapGroup("/api/v1/spaces/{spaceId:guid}/journal-entries")
            .WithTags("Ledger");

        var postEndpoint = group.MapPost("/", PostJournalEntryAsync)
            .WithName("PostJournalEntry")
            .Produces<PostingResponse>(StatusCodes.Status201Created)
            .Produces<LedgerProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(postEndpoint, "ledger.post");

        var reverseEndpoint = group.MapPost("/{entryId:guid}/reverse", ReverseJournalEntryAsync)
            .WithName("ReverseJournalEntry")
            .Produces<PostingResponse>(StatusCodes.Status201Created)
            .Produces<LedgerProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(reverseEndpoint, "ledger.reverse");

        var reportGroup = endpoints.MapGroup("/api/v1/spaces/{spaceId:guid}")
            .WithTags("Ledger");
        var accountsEndpoint = reportGroup.MapGet("/accounts", GetAccountsAsync)
            .WithName("GetAccounts")
            .WithTags("ChartOfAccounts")
            .Produces<AccountCatalogReport>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(accountsEndpoint, "ledger.read");
        var groupsEndpoint = reportGroup.MapGet("/groups", GetGroupsAsync)
            .WithName("GetGroups")
            .WithTags("ChartOfAccounts")
            .Produces<GroupCatalogReport>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(groupsEndpoint, "ledger.read");

        var accountExportEndpoint = reportGroup.MapGet("/accounts/export", ExportAccountsAsync)
            .WithName("ExportAccounts")
            .WithTags("ChartOfAccounts")
            .Produces<string>(StatusCodes.Status200OK, "text/csv")
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(accountExportEndpoint, "ledger.read");
        var groupExportEndpoint = reportGroup.MapGet("/groups/export", ExportGroupsAsync)
            .WithName("ExportAccountGroups")
            .WithTags("ChartOfAccounts")
            .Produces<string>(StatusCodes.Status200OK, "text/csv")
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(groupExportEndpoint, "ledger.read");

        var accountWriteGroup = reportGroup.MapGroup("/accounts")
            .WithTags("ChartOfAccounts");
        var createAccountEndpoint = accountWriteGroup.MapPost("/", CreateAccountAsync)
            .WithName("CreateAccount")
            .Produces<AccountView>(StatusCodes.Status201Created)
            .Produces<LedgerProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(createAccountEndpoint, "accounts.manage");
        var updateAccountEndpoint = accountWriteGroup.MapPatch("/{accountId:guid}", UpdateAccountAsync)
            .WithName("UpdateAccount")
            .Produces<AccountView>(StatusCodes.Status200OK)
            .Produces<LedgerProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(updateAccountEndpoint, "accounts.manage");
        var activateAccountEndpoint = accountWriteGroup.MapPost("/{accountId:guid}/activate", ActivateAccountAsync)
            .WithName("ActivateAccount")
            .Produces<AccountView>(StatusCodes.Status200OK)
            .Produces<LedgerProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(activateAccountEndpoint, "accounts.manage");
        var deactivateAccountEndpoint = accountWriteGroup.MapPost("/{accountId:guid}/deactivate", DeactivateAccountAsync)
            .WithName("DeactivateAccount")
            .Produces<AccountView>(StatusCodes.Status200OK)
            .Produces<LedgerProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(deactivateAccountEndpoint, "accounts.manage");
        var importAccountsEndpoint = accountWriteGroup.MapPost("/import", ImportAccountsAsync)
            .WithName("ImportAccounts")
            .Produces<ImportReport>(StatusCodes.Status200OK)
            .Produces<ImportReport>(StatusCodes.Status422UnprocessableEntity)
            .Produces<LedgerProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(importAccountsEndpoint, "accounts.manage");

        var groupWriteGroup = reportGroup.MapGroup("/groups")
            .WithTags("ChartOfAccounts");
        var createGroupEndpoint = groupWriteGroup.MapPost("/", CreateGroupAsync)
            .WithName("CreateAccountGroup")
            .Produces<GroupView>(StatusCodes.Status201Created)
            .Produces<LedgerProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(createGroupEndpoint, "accounts.manage");
        var updateGroupEndpoint = groupWriteGroup.MapPatch("/{groupId:guid}", UpdateGroupAsync)
            .WithName("UpdateAccountGroup")
            .Produces<GroupView>(StatusCodes.Status200OK)
            .Produces<LedgerProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(updateGroupEndpoint, "accounts.manage");
        var importGroupsEndpoint = groupWriteGroup.MapPost("/import", ImportGroupsAsync)
            .WithName("ImportAccountGroups")
            .Produces<ImportReport>(StatusCodes.Status200OK)
            .Produces<ImportReport>(StatusCodes.Status422UnprocessableEntity)
            .Produces<LedgerProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(importGroupsEndpoint, "accounts.manage");
        var accountLedgerEndpoint = reportGroup.MapGet(
                "/reports/account-ledger/{accountId:guid}",
                GetAccountLedgerAsync)
            .WithName("GetAccountLedger")
            .Produces<AccountLedgerReport>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(accountLedgerEndpoint, "ledger.read");
        MapReportEndpoint<TrialBalanceReport>(reportGroup.MapGet("/reports/trial-balance", GetTrialBalanceAsync), "GetTrialBalance");
        MapReportEndpoint<BalanceSheetReport>(reportGroup.MapGet("/reports/balance-sheet", GetBalanceSheetAsync), "GetBalanceSheet");
        MapReportEndpoint<IncomeStatementReport>(reportGroup.MapGet("/reports/income-statement", GetIncomeStatementAsync), "GetIncomeStatement");
        MapReportEndpoint<DashboardSummaryReport>(reportGroup.MapGet("/reports/dashboard", GetDashboardSummaryAsync), "GetDashboardSummary");
        MapReportEndpoint<IntegrityReport>(reportGroup.MapGet("/integrity", GetIntegrityAsync), "GetIntegrity");
        endpoints.MapPeriodEndpoints(configureAuthorization);

        void MapReportEndpoint<TResponse>(RouteHandlerBuilder endpoint, string name)
        {
            endpoint.WithName(name)
            .Produces<TResponse>(StatusCodes.Status200OK)
                .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
                .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
            configureAuthorization?.Invoke(endpoint, "ledger.read");
        }

        return endpoints;
    }

    private static Task<TrialBalanceReport> GetTrialBalanceAsync(Guid spaceId, [FromServices] ILedgerReportService service, CancellationToken cancellationToken) =>
        service.GetTrialBalanceAsync(spaceId, cancellationToken);

    private static Task<AccountCatalogReport> GetAccountsAsync(Guid spaceId, [FromServices] IAccountCatalogService service, CancellationToken cancellationToken) =>
        service.GetAccountsAsync(spaceId, cancellationToken);

    private static Task<GroupCatalogReport> GetGroupsAsync(Guid spaceId, [FromServices] IGroupCatalogService service, CancellationToken cancellationToken) =>
        service.GetGroupsAsync(spaceId, cancellationToken);

    private static async Task<IResult> ExportAccountsAsync(
        Guid spaceId,
        [FromServices] IAccountCatalogService accountService,
        [FromServices] IGroupCatalogService groupService,
        CancellationToken cancellationToken)
    {
        var accounts = await accountService.GetAccountsAsync(spaceId, cancellationToken).ConfigureAwait(false);
        var groups = await groupService.GetGroupsAsync(spaceId, cancellationToken).ConfigureAwait(false);
        var names = groups.Groups.ToDictionary(group => group.Id, group => group.Name);
        var rows = accounts.Accounts.Select(account => new AccountImportRow(
            account.Kind,
            account.Code,
            account.Name,
            account.Currency,
            names.GetValueOrDefault(account.GroupId),
            account.IsActive,
            account.ValidFrom,
            account.ValidTo,
            account.FxPolicy));
        return Results.File(Encoding.UTF8.GetBytes(AccountCsv.WriteAccounts(rows)), "text/csv", "accounts.csv");
    }

    private static async Task<IResult> ExportGroupsAsync(
        Guid spaceId,
        [FromServices] IGroupCatalogService service,
        CancellationToken cancellationToken)
    {
        var groups = await service.GetGroupsAsync(spaceId, cancellationToken).ConfigureAwait(false);
        var names = groups.Groups.ToDictionary(group => group.Id, group => group.Name);
        var rows = groups.Groups
            .OrderBy(group => group.RangeStart)
            .ThenBy(group => group.Id)
            .Select(group => new GroupImportRow(
                group.Name,
                group.RangeStart,
                group.RangeEnd,
                group.ParentId is Guid parentId ? names.GetValueOrDefault(parentId) : null,
                group.FxPolicy));
        return Results.File(Encoding.UTF8.GetBytes(AccountCsv.WriteGroups(rows)), "text/csv", "account-groups.csv");
    }

    private static async Task<IResult> CreateAccountAsync(Guid spaceId, CreateAccountCommand request, [FromServices] IAccountManagementService service, HttpRequest httpRequest, CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpRequest.HttpContext, out var actorId)) return AuthorizationFailure();
        if (!TryGetIdempotencyKey(httpRequest, out var idempotencyKey, out var keyError)) return keyError!;
        var outcome = await service.CreateAccountAsync(spaceId, actorId, idempotencyKey!, request, cancellationToken);
        return ToAccountManagementResult(spaceId, outcome, httpRequest.HttpContext.Response, "accounts");
    }

    private static async Task<IResult> UpdateAccountAsync(Guid spaceId, Guid accountId, UpdateAccountCommand request, [FromServices] IAccountManagementService service, HttpRequest httpRequest, CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpRequest.HttpContext, out var actorId)) return AuthorizationFailure();
        if (!TryGetIdempotencyKey(httpRequest, out var idempotencyKey, out var keyError)) return keyError!;
        var outcome = await service.UpdateAccountAsync(spaceId, actorId, accountId, idempotencyKey!, request, cancellationToken);
        return ToAccountManagementResult(spaceId, outcome, httpRequest.HttpContext.Response, "accounts");
    }

    private static Task<IResult> ActivateAccountAsync(Guid spaceId, Guid accountId, [FromServices] IAccountManagementService service, HttpRequest httpRequest, CancellationToken cancellationToken) =>
        ExecuteAccountActiveWriteAsync(spaceId, accountId, true, service, httpRequest, cancellationToken);

    private static Task<IResult> DeactivateAccountAsync(Guid spaceId, Guid accountId, [FromServices] IAccountManagementService service, HttpRequest httpRequest, CancellationToken cancellationToken) =>
        ExecuteAccountActiveWriteAsync(spaceId, accountId, false, service, httpRequest, cancellationToken);

    private static async Task<IResult> CreateGroupAsync(Guid spaceId, CreateGroupCommand request, [FromServices] IAccountManagementService service, HttpRequest httpRequest, CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpRequest.HttpContext, out var actorId)) return AuthorizationFailure();
        if (!TryGetIdempotencyKey(httpRequest, out var idempotencyKey, out var keyError)) return keyError!;
        var outcome = await service.CreateAccountGroupAsync(spaceId, actorId, idempotencyKey!, request, cancellationToken);
        return ToAccountManagementResult(spaceId, outcome, httpRequest.HttpContext.Response, "groups");
    }

    private static async Task<IResult> UpdateGroupAsync(Guid spaceId, Guid groupId, UpdateGroupCommand request, [FromServices] IAccountManagementService service, HttpRequest httpRequest, CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpRequest.HttpContext, out var actorId)) return AuthorizationFailure();
        if (!TryGetIdempotencyKey(httpRequest, out var idempotencyKey, out var keyError)) return keyError!;
        var outcome = await service.UpdateAccountGroupAsync(spaceId, actorId, groupId, idempotencyKey!, request, cancellationToken);
        return ToAccountManagementResult(spaceId, outcome, httpRequest.HttpContext.Response, "groups");
    }

    private static async Task<IResult> ImportAccountsAsync(
        Guid spaceId,
        [FromServices] IAccountImportService service,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpRequest.HttpContext, out var actorId)) return AuthorizationFailure();
        if (!TryGetIdempotencyKey(httpRequest, out var idempotencyKey, out var keyError)) return keyError!;
        try
        {
            var rows = await ReadAccountRowsAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            var outcome = await service.ImportAccountsAsync(spaceId, actorId, idempotencyKey!, rows, cancellationToken).ConfigureAwait(false);
            return ToAccountImportResult(outcome, httpRequest.HttpContext.Response);
        }
        catch (FormatException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "The import file is invalid.", detail: exception.Message,
                type: "https://leafledger.dev/problems/account-import");
        }
    }

    private static async Task<IResult> ImportGroupsAsync(
        Guid spaceId,
        [FromServices] IAccountImportService service,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpRequest.HttpContext, out var actorId)) return AuthorizationFailure();
        if (!TryGetIdempotencyKey(httpRequest, out var idempotencyKey, out var keyError)) return keyError!;
        try
        {
            var rows = await ReadGroupRowsAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            var outcome = await service.ImportGroupsAsync(spaceId, actorId, idempotencyKey!, rows, cancellationToken).ConfigureAwait(false);
            return ToAccountImportResult(outcome, httpRequest.HttpContext.Response);
        }
        catch (FormatException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "The import file is invalid.", detail: exception.Message,
                type: "https://leafledger.dev/problems/account-import");
        }
    }

    private static async Task<IReadOnlyList<CsvImportRow<AccountImportRow>>> ReadAccountRowsAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            var rows = JsonSerializer.Deserialize<AccountImportRequest>(body, JsonOptions)?.Rows;
            if (rows is null)
            {
                rows = JsonSerializer.Deserialize<AccountImportRow[]>(body, JsonOptions);
            }

            if (rows is null)
            {
                throw new FormatException("JSON import rows are required.");
            }

            return rows.Select((row, index) => new CsvImportRow<AccountImportRow>(index + 1, row, [])).ToArray();
        }

        return AccountCsv.ReadAccountsWithWarnings(body).Rows;
    }

    private static async Task<IReadOnlyList<CsvImportRow<GroupImportRow>>> ReadGroupRowsAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            var rows = JsonSerializer.Deserialize<GroupImportRequest>(body, JsonOptions)?.Rows;
            if (rows is null)
            {
                rows = JsonSerializer.Deserialize<GroupImportRow[]>(body, JsonOptions);
            }

            if (rows is null)
            {
                throw new FormatException("JSON import rows are required.");
            }

            return rows.Select((row, index) => new CsvImportRow<GroupImportRow>(index + 1, row, [])).ToArray();
        }

        return AccountCsv.ReadGroupsWithWarnings(body).Rows;
    }

    private static IResult ToAccountImportResult(AccountImportOutcome outcome, HttpResponse response)
    {
        if (outcome.IsReplay)
        {
            response.Headers["Idempotent-Replayed"] = "true";
            return Results.Content(outcome.Replay!.Body, "application/json", Encoding.UTF8, outcome.Replay.Status);
        }

        if (outcome.Report is not null)
        {
            return Results.Json(outcome.Report, statusCode: outcome.SuccessStatus);
        }

        var failure = outcome.Failure!;
        var problem = new ProblemDetails
        {
            Status = failure.Status,
            Title = "The account import could not be completed.",
            Type = "https://leafledger.dev/problems/account-import",
        };
        problem.Extensions["errors"] = failure.Issues;
        problem.Extensions["issues"] = failure.Issues;
        return Results.Problem(problem);
    }

    private static async Task<IResult> ExecuteAccountActiveWriteAsync(Guid spaceId, Guid accountId, bool active, IAccountManagementService service, HttpRequest httpRequest, CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpRequest.HttpContext, out var actorId)) return AuthorizationFailure();
        if (!TryGetIdempotencyKey(httpRequest, out var idempotencyKey, out var keyError)) return keyError!;
        var outcome = await service.SetAccountActiveAsync(spaceId, actorId, accountId, active, idempotencyKey!, cancellationToken);
        return ToAccountManagementResult(spaceId, outcome, httpRequest.HttpContext.Response, "accounts");
    }

    private static IResult ToAccountManagementResult<T>(Guid spaceId, AccountManagementOutcome<T> outcome, HttpResponse response, string resourceName)
        where T : class
    {
        if (outcome.IsReplay)
        {
            response.Headers["Idempotent-Replayed"] = "true";
            return Results.Content(outcome.Replay!.Body, "application/json", Encoding.UTF8, outcome.Replay.Status);
        }
        if (outcome.IsSuccess)
        {
            var status = outcome.SuccessStatus;
            return status == StatusCodes.Status201Created
                ? Results.Created($"/api/v1/spaces/{spaceId}/{resourceName}/{outcome.Value!.GetType().GetProperty("Id")!.GetValue(outcome.Value)}", outcome.Value)
                : Results.Ok(outcome.Value);
        }
        var failure = outcome.Failure!;
        var problem = new ProblemDetails
        {
            Status = failure.Status,
            Title = "The account management request could not be completed.",
            Type = "https://leafledger.dev/problems/account-management",
        };
        problem.Extensions["errors"] = failure.Issues;
        problem.Extensions["issues"] = failure.Issues;
        return Results.Problem(problem);
    }

    private static Task<AccountLedgerReport> GetAccountLedgerAsync(
        Guid spaceId,
        Guid accountId,
        [FromQuery(Name = "from")] DateOnly? from,
        [FromQuery(Name = "to")] DateOnly? to,
        [FromServices] IAccountLedgerService service,
        CancellationToken cancellationToken) =>
        service.GetAccountLedgerAsync(spaceId, accountId, from, to, cancellationToken);

    private static Task<BalanceSheetReport> GetBalanceSheetAsync(Guid spaceId, [FromServices] ILedgerReportService service, CancellationToken cancellationToken) =>
        service.GetBalanceSheetAsync(spaceId, cancellationToken);

    private static Task<IncomeStatementReport> GetIncomeStatementAsync(Guid spaceId, [FromServices] ILedgerReportService service, CancellationToken cancellationToken) =>
        service.GetIncomeStatementAsync(spaceId, cancellationToken);

    private static Task<DashboardSummaryReport> GetDashboardSummaryAsync(Guid spaceId, [FromServices] IDashboardService service, CancellationToken cancellationToken) =>
        service.GetDashboardSummaryAsync(spaceId, cancellationToken);

    private static Task<IntegrityReport> GetIntegrityAsync(Guid spaceId, [FromServices] ILedgerReportService service, CancellationToken cancellationToken) =>
        service.GetIntegrityAsync(spaceId, cancellationToken);

    private static async Task<IResult> PostJournalEntryAsync(
        Guid spaceId,
        PostJournalEntryRequest request,
        [FromServices] IJournalPostingService service,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpRequest.HttpContext, out var actorId))
        {
            return AuthorizationFailure();
        }

        if (!TryGetIdempotencyKey(httpRequest, out var idempotencyKey, out var keyError))
        {
            return keyError!;
        }

        var outcome = await service.PostAsync(
            new PostJournalEntryCommand(spaceId, actorId, request.Date, request.Description, request.Reference, request.Lines, idempotencyKey),
            cancellationToken).ConfigureAwait(false);
        return ToHttpResult(spaceId, outcome, httpRequest.HttpContext.Response);
    }

    private static async Task<IResult> ReverseJournalEntryAsync(
        Guid spaceId,
        Guid entryId,
        ReverseJournalEntryRequest request,
        [FromServices] IJournalPostingService service,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpRequest.HttpContext, out var actorId))
        {
            return AuthorizationFailure();
        }

        if (!TryGetIdempotencyKey(httpRequest, out var idempotencyKey, out var keyError))
        {
            return keyError!;
        }

        var outcome = await service.ReverseAsync(
            new ReverseJournalEntryCommand(spaceId, actorId, entryId, request.Date, idempotencyKey),
            cancellationToken).ConfigureAwait(false);
        return ToHttpResult(spaceId, outcome, httpRequest.HttpContext.Response);
    }

    private static IResult ToHttpResult(Guid spaceId, PostingOutcome outcome, HttpResponse response)
    {
        if (outcome.IsReplay)
        {
            response.Headers.ContentType = "application/json; charset=utf-8";
            response.Headers["Idempotent-Replayed"] = "true";
            return Results.Content(outcome.Replay!.Body, "application/json", Encoding.UTF8, outcome.Replay.Status);
        }

        if (outcome.IsSuccess)
        {
            return Results.Created($"/api/v1/spaces/{spaceId}/journal-entries/{outcome.Value!.Id}", outcome.Value);
        }

        var failure = outcome.Failure!;
        var problem = new ProblemDetails
        {
            Status = failure.Status,
            Title = failure.Status == StatusCodes.Status404NotFound ? "Journal entry not found." : "The journal entry could not be posted.",
            Type = "https://leafledger.dev/problems/journal-entry",
        };
        problem.Extensions["errors"] = failure.Issues;
        problem.Extensions["issues"] = failure.Issues;
        return Results.Problem(problem);
    }

    private static bool TryGetIdempotencyKey(HttpRequest request, out string? key, out IResult? error)
    {
        key = request.Headers.TryGetValue("Idempotency-Key", out var value) ? value.ToString() : null;
        if (string.IsNullOrWhiteSpace(key))
        {
            error = IdempotencyHeaderProblem("idempotency.key_required", "Idempotency-Key is required for write requests.");
            return false;
        }

        if (!Ulid.TryParse(key, out _))
        {
            error = IdempotencyHeaderProblem("idempotency.key_invalid", "Idempotency-Key must be a valid ULID.");
            return false;
        }

        error = null;
        return true;
    }

    private static IResult IdempotencyHeaderProblem(string code, string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The idempotency key is invalid.",
            detail: detail,
            type: "https://leafledger.dev/problems/idempotency",
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static bool TryGetActor(HttpContext context, out Guid actorId)
    {
        if (context.Items.TryGetValue(LedgerRequestContext.ActorIdItemKey, out var value) && value is Guid resolvedActor)
        {
            actorId = resolvedActor;
            return true;
        }

        actorId = Guid.Empty;
        return false;
    }

    private static IResult AuthorizationFailure() =>
        Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Authentication is required.",
            type: "https://leafledger.dev/problems/authorization",
            extensions: new Dictionary<string, object?> { ["code"] = "auth.unauthenticated" });
}

public sealed class LedgerProblemDetails
{
    public string? Type { get; init; }

    public string? Title { get; init; }

    public int? Status { get; init; }

    public string? Detail { get; init; }

    public string? Instance { get; init; }

    public LedgerProblemError[]? Errors { get; init; }

    public LedgerProblemIssue[]? Issues { get; init; }
}

public sealed record LedgerProblemError(string Code, string Message, int? Line);

public sealed record LedgerProblemIssue(string Code, string Message, int? Line);