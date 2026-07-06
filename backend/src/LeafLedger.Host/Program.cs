#pragma warning disable CA1861

var builder = WebApplication.CreateBuilder(args);

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

#pragma warning restore CA1861

var app = builder.Build();

// GET /health → liveness (always 200 when process is up)
app.MapHealthChecks("/health", new() { Predicate = r => !r.Tags.Contains("ready") });

// GET /health/ready → readiness (200 when DB is reachable, 503 otherwise)
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });

app.Run();
