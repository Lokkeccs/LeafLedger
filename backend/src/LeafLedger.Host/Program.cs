using LeafLedger.Host.Authorization;
using LeafLedger.Modules.Ledger.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;

#pragma warning disable CA1861

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddMetrics();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddScoped<ILicenseEntitlement, AllowAllLicenseEntitlement>();
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options => AuthenticationConfiguration.ConfigureJwtBearer(
        options,
        builder.Configuration,
        builder.Environment.IsDevelopment()));
builder.Services.AddAuthorization();

// OpenAPI: the "v1" document is the single client contract (P1-WP04).
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "LeafLedger API";
        document.Info.Version = "v1";
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes["bearerAuth"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
        };

        foreach (var path in document.Paths.Where(item =>
                     item.Key.StartsWith("/api/v1/spaces/", StringComparison.Ordinal)))
        {
            foreach (var operation in path.Value.Operations.Values)
            {
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "bearerAuth",
                        },
                    }] = Array.Empty<string>(),
                });

                if (operation.OperationId is "PostJournalEntry" or "ReverseJournalEntry" or "CreatePeriod" or "ClosePeriod" or "ReopenPeriod" or "LockPeriod")
                {
                    operation.Parameters ??= new List<OpenApiParameter>();
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "Idempotency-Key",
                        In = ParameterLocation.Header,
                        Required = true,
                        Schema = new OpenApiSchema { Type = "string" },
                    });
                }
            }
        }

        return Task.CompletedTask;
    });
});

// Liveness (self only)
builder.Services.AddHealthChecks();

// Readiness (includes DB)
var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddHealthChecks()
        .AddNpgSql(
            connectionString,
            name: "postgres",
            tags: new[] { "ready" }
        );
}

// Ledger persistence (P2-WP02). EF usage stays inside the module's Infrastructure via
// these extension methods, so the Host takes no direct EF Core dependency.
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddLedgerModule(connectionString);
}

#pragma warning restore CA1861

var app = builder.Build();

// Apply Ledger migrations on startup for local dev / compose. Production migrations run
// through the deploy pipeline, not here.
if (app.Environment.IsDevelopment() && !string.IsNullOrEmpty(connectionString))
{
    await app.Services.MigrateLedgerAsync();
    await app.Services.SeedAsync(builder.Configuration);
}

// Serve the OpenAPI document (GET /openapi/v1.json) in development; the
// canonical contract is emitted at build time regardless (see csproj).
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// GET /health → liveness (always 200 when process is up)
app.MapHealthChecks("/health", new() { Predicate = r => !r.Tags.Contains("ready") });

// GET /health/ready → readiness (200 when DB is reachable, 503 otherwise)
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });

// GET /api/v1/meta → API metadata. Anchor endpoint for the contract pipeline
// (P1-WP04); real business endpoints arrive in their own WPs.
app.MapGet("/api/v1/meta", () => new MetaResponse("LeafLedger", "v1"))
    .WithName("GetMeta")
    .WithTags("Meta");

app.UseAuthentication();
app.UseAuthorization();

var requiredScope = AuthenticationConfiguration.GetRequiredScope(builder.Configuration);
app.MapLedgerEndpoints((endpoint, permission) => endpoint.RequireSpacePermission(permission, requiredScope));

app.Run();

/// <summary>API metadata returned by <c>GET /api/v1/meta</c>.</summary>
/// <param name="Name">Human-readable API name.</param>
/// <param name="Version">API contract version (e.g. <c>v1</c>).</param>
internal sealed record MetaResponse(string Name, string Version);

public partial class Program;
