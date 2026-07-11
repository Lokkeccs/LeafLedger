using LeafLedger.Modules.Ledger.Infrastructure;

#pragma warning disable CA1861

var builder = WebApplication.CreateBuilder(args);

// OpenAPI: the "v1" document is the single client contract (P1-WP04).
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "LeafLedger API";
        document.Info.Version = "v1";
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

app.Run();

/// <summary>API metadata returned by <c>GET /api/v1/meta</c>.</summary>
/// <param name="Name">Human-readable API name.</param>
/// <param name="Version">API contract version (e.g. <c>v1</c>).</param>
internal sealed record MetaResponse(string Name, string Version);
