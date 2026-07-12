using System.Security.Claims;
using System.Text;
using LeafLedger.Modules.Ledger.Application.Periods;
using LeafLedger.Modules.Ledger.Domain.Periods;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LeafLedger.Modules.Ledger.Infrastructure;

public static class PeriodEndpoints
{
    public static void MapPeriodEndpoints(this IEndpointRouteBuilder endpoints, Action<RouteHandlerBuilder, string>? configureAuthorization)
    {
        var group = endpoints.MapGroup("/api/v1/spaces/{spaceId:guid}/periods").WithTags("Periods");

        var create = group.MapPost("/", CreateAsync).WithName("CreatePeriod")
            .Produces<PeriodResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json");
        configureAuthorization?.Invoke(create, "period.manage");

        MapTransition(group, "/{periodId:guid}/close", "ClosePeriod", PeriodState.Closed, configureAuthorization);
        MapTransition(group, "/{periodId:guid}/reopen", "ReopenPeriod", PeriodState.Open, configureAuthorization);
        MapTransition(group, "/{periodId:guid}/lock", "LockPeriod", PeriodState.Locked, configureAuthorization);

        var list = group.MapGet("/", ListAsync).WithName("ListPeriods")
            .Produces<IReadOnlyList<PeriodResponse>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");
        configureAuthorization?.Invoke(list, "period.manage");
    }

    private static void MapTransition(RouteGroupBuilder group, string route, string name, PeriodState target, Action<RouteHandlerBuilder, string>? configureAuthorization)
    {
        var endpoint = group.MapPost(route, (Guid spaceId, Guid periodId, [FromServices] IPeriodLifecycleService service, HttpRequest request, CancellationToken cancellationToken) =>
            TransitionAsync(spaceId, periodId, target, service, request, cancellationToken))
            .WithName(name)
            .Produces<PeriodResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
            .Produces<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json");
        configureAuthorization?.Invoke(endpoint, "period.manage");
    }

    private static async Task<IResult> CreateAsync(Guid spaceId, CreatePeriodRequest request, [FromServices] IPeriodLifecycleService service, HttpRequest httpRequest, CancellationToken cancellationToken)
    {
        IResult? keyError = null;
        if (!TryGetActor(httpRequest.HttpContext, out var actorId) || !TryGetKey(httpRequest, out var key, out keyError))
        {
            return keyError ?? AuthorizationFailure();
        }

        return ToHttpResult(spaceId, await service.CreateAsync(spaceId, actorId, request, key!, cancellationToken).ConfigureAwait(false), httpRequest.HttpContext.Response);
    }

    private static async Task<IResult> TransitionAsync(Guid spaceId, Guid periodId, PeriodState target, IPeriodLifecycleService service, HttpRequest httpRequest, CancellationToken cancellationToken)
    {
        IResult? keyError = null;
        if (!TryGetActor(httpRequest.HttpContext, out var actorId) || !TryGetKey(httpRequest, out var key, out keyError))
        {
            return keyError ?? AuthorizationFailure();
        }

        return ToHttpResult(spaceId, await service.TransitionAsync(spaceId, actorId, periodId, target, key!, cancellationToken).ConfigureAwait(false), httpRequest.HttpContext.Response);
    }

    private static async Task<IReadOnlyList<PeriodResponse>> ListAsync(Guid spaceId, [FromServices] IPeriodLifecycleService service, HttpRequest httpRequest, CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpRequest.HttpContext, out var actorId))
        {
            return [];
        }

        return await service.ListAsync(spaceId, actorId, cancellationToken).ConfigureAwait(false);
    }

    private static IResult ToHttpResult(Guid spaceId, PeriodOutcome outcome, HttpResponse response)
    {
        if (outcome.IsReplay)
        {
            response.Headers["Idempotent-Replayed"] = "true";
            return Results.Content(outcome.Replay!.Body, "application/json", Encoding.UTF8, outcome.Replay.Status);
        }

        if (outcome.IsSuccess)
        {
            return outcome.SuccessStatus == StatusCodes.Status201Created
                ? Results.Created($"/api/v1/spaces/{spaceId}/periods/{outcome.Value!.Id}", outcome.Value)
                : Results.Json(outcome.Value, statusCode: outcome.SuccessStatus);
        }

        var failure = outcome.Failure!;
        var problem = new ProblemDetails
        {
            Status = failure.Status,
            Title = "The accounting period operation could not be completed.",
            Type = "https://leafledger.dev/problems/period",
        };
        problem.Extensions["errors"] = failure.Issues;
        problem.Extensions["issues"] = failure.Issues;
        return Results.Problem(problem);
    }

    private static bool TryGetKey(HttpRequest request, out string? key, out IResult? error)
    {
        key = request.Headers.TryGetValue("Idempotency-Key", out var value) ? value.ToString() : null;
        if (string.IsNullOrWhiteSpace(key))
        {
            error = Results.Problem(statusCode: 400, detail: "The idempotency key is required.", extensions: new Dictionary<string, object?> { ["code"] = "idempotency.key_required" });
            return false;
        }

        if (!Ulid.TryParse(key, out _))
        {
            error = Results.Problem(statusCode: 400, detail: "The idempotency key is invalid.", extensions: new Dictionary<string, object?> { ["code"] = "idempotency.key_invalid" });
            return false;
        }

        error = null;
        return true;
    }

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

    private static IResult AuthorizationFailure() => Results.Problem(statusCode: 401, detail: "Authentication is required.", extensions: new Dictionary<string, object?> { ["code"] = "auth.unauthenticated" });
}