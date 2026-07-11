using LeafLedger.Modules.Ledger.Application.Posting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LeafLedger.Modules.Ledger.Infrastructure;

public static class LedgerEndpoints
{
    public static IEndpointRouteBuilder MapLedgerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/spaces/{spaceId:guid}/journal-entries")
            .WithTags("Ledger");

        group.MapPost("/", PostJournalEntryAsync)
            .WithName("PostJournalEntry")
            .Produces<PostingResponse>(StatusCodes.Status201Created)
            .Produces<LedgerProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json");

        group.MapPost("/{entryId:guid}/reverse", ReverseJournalEntryAsync)
            .WithName("ReverseJournalEntry")
            .Produces<PostingResponse>(StatusCodes.Status201Created)
            .Produces<LedgerProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<LedgerProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json");

        return endpoints;
    }

    private static async Task<IResult> PostJournalEntryAsync(
        Guid spaceId,
        PostJournalEntryRequest request,
        [FromServices] IJournalPostingService service,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpRequest, out var actorId, out var actorError))
        {
            return actorError!;
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
        if (!TryGetActor(httpRequest, out var actorId, out var actorError))
        {
            return actorError!;
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

    private static bool TryGetActor(HttpRequest request, out Guid actorId, out IResult? error)
    {
        if (request.Headers.TryGetValue("X-Actor-Id", out var value) && Guid.TryParse(value, out actorId))
        {
            error = null;
            return true;
        }

        actorId = Guid.Empty;
        error = Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The X-Actor-Id header must contain a GUID.",
            type: "https://leafledger.dev/problems/request-invalid");
        return false;
    }
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