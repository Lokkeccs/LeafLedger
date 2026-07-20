using LeafLedger.Modules.Ledger.Application.Posting;
using LeafLedger.Modules.Ledger.Application.Reporting;
using LeafLedger.Modules.Ledger.Application.Accounts;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LeafLedger.Modules.Ledger.Infrastructure;

public static class LedgerEndpoints
{
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