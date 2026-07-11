using LeafLedger.Modules.Ledger.Application.Posting;
using LeafLedger.Modules.Ledger.Application.Reporting;
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
            .Produces<LedgerProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(postEndpoint, "ledger.post");

        var reverseEndpoint = group.MapPost("/{entryId:guid}/reverse", ReverseJournalEntryAsync)
            .WithName("ReverseJournalEntry")
            .Produces<PostingResponse>(StatusCodes.Status201Created)
            .Produces<LedgerProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(reverseEndpoint, "ledger.reverse");

        var reportGroup = endpoints.MapGroup("/api/v1/spaces/{spaceId:guid}")
            .WithTags("Ledger");
        MapReportEndpoint<TrialBalanceReport>(reportGroup.MapGet("/reports/trial-balance", GetTrialBalanceAsync), "GetTrialBalance");
        MapReportEndpoint<BalanceSheetReport>(reportGroup.MapGet("/reports/balance-sheet", GetBalanceSheetAsync), "GetBalanceSheet");
        MapReportEndpoint<IncomeStatementReport>(reportGroup.MapGet("/reports/income-statement", GetIncomeStatementAsync), "GetIncomeStatement");
        MapReportEndpoint<IntegrityReport>(reportGroup.MapGet("/integrity", GetIntegrityAsync), "GetIntegrity");

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

    private static Task<BalanceSheetReport> GetBalanceSheetAsync(Guid spaceId, [FromServices] ILedgerReportService service, CancellationToken cancellationToken) =>
        service.GetBalanceSheetAsync(spaceId, cancellationToken);

    private static Task<IncomeStatementReport> GetIncomeStatementAsync(Guid spaceId, [FromServices] ILedgerReportService service, CancellationToken cancellationToken) =>
        service.GetIncomeStatementAsync(spaceId, cancellationToken);

    private static Task<IntegrityReport> GetIntegrityAsync(Guid spaceId, [FromServices] ILedgerReportService service, CancellationToken cancellationToken) =>
        service.GetIntegrityAsync(spaceId, cancellationToken);

    private static async Task<IResult> PostJournalEntryAsync(
        Guid spaceId,
        PostJournalEntryRequest request,
        [FromServices] IJournalPostingService service,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpRequest.HttpContext.User, out var actorId))
        {
            return AuthorizationFailure();
        }

        var outcome = await service.PostAsync(
            new PostJournalEntryCommand(spaceId, actorId, request.Date, request.Description, request.Reference, request.Lines, GetIdempotencyKey(httpRequest)),
            cancellationToken).ConfigureAwait(false);
        return ToHttpResult(spaceId, outcome);
    }

    private static async Task<IResult> ReverseJournalEntryAsync(
        Guid spaceId,
        Guid entryId,
        ReverseJournalEntryRequest request,
        [FromServices] IJournalPostingService service,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpRequest.HttpContext.User, out var actorId))
        {
            return AuthorizationFailure();
        }

        var outcome = await service.ReverseAsync(
            new ReverseJournalEntryCommand(spaceId, actorId, entryId, request.Date, GetIdempotencyKey(httpRequest)),
            cancellationToken).ConfigureAwait(false);
        return ToHttpResult(spaceId, outcome);
    }

    private static IResult ToHttpResult(Guid spaceId, PostingOutcome outcome)
    {
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

    private static string? GetIdempotencyKey(HttpRequest request) =>
        request.Headers.TryGetValue("Idempotency-Key", out var value) ? value.ToString() : null;

    private static bool TryGetActor(ClaimsPrincipal principal, out Guid actorId)
    {
        var subject = principal.FindFirst("oid")?.Value ?? principal.FindFirst("sub")?.Value;
        return Guid.TryParse(subject, out actorId);
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