using BackendApplication.Configuration;
using BackendApplication.Extensions;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Log to the console as compact single-line entries prefixed with a HH:mm:ss timestamp.
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});

// Composition root: declares every service the DI container can hand out. Nothing is
// constructed yet - each call only records a registration, and the container resolves them
// lazily once builder.Build() runs.
//
// builder.Configuration is the merged IConfiguration that CreateBuilder assembled from
// appsettings.json, appsettings.{Environment}.json, user secrets (Development), environment
// variables and the command-line args - later sources winning. It is passed only to the
// methods that read settings, which look up sections such as "Database", "Jwt" and "Cors".

builder.Services
    .AddApplicationOptions(builder.Configuration)       // Binds/validates DatabaseOptions + JwtOptions, failing fast at startup
    .AddPersistence(builder.Configuration)              // Npgsql data source, pooled TasksDbContext, EF interceptors, repositories
    .AddApplicationServices()                           // Task and auth services plus the FluentValidation validators
    .AddJwtAuthentication(builder.Configuration)        // JWT bearer validation plus the LeadOrAdmin/AdminOnly/VerifiedUser policies
    .AddApiVersioningSupport()                          // URL-segment versioning, with header/query/media-type readers as fallbacks
    .AddApiControllers()                                // Controllers, global filters, JSON serialisation rules, ProblemDetails
    .AddOpenApiDocuments()                              // One OpenAPI document per API version
    .AddRateLimiting()                                  // Global token-bucket limiter + a stricter fixed window on auth endpoints
    .AddCorsPolicies(builder.Configuration)             // The "Default" CORS policy for the configured browser origins
    .AddApplicationHealthChecks(builder.Configuration); // PostgreSQL probe surfaced through the readiness endpoint

var app = builder.Build();

// ---------------------------------------------------------------------------------------
//  THE PIPELINE. Order is behaviour, not style.
//
//  Each of these wraps everything below it, so the exception handler goes first (it must
//  see every failure), correlation before logging (so every log line carries the id), and
//  authentication before authorisation (you cannot check a role before knowing who it is).
// ---------------------------------------------------------------------------------------

app.UseGlobalExceptionHandling();

app.UseCorrelationId();

app.UseRequestLogging();

// Inside a container TLS is terminated at the ingress, so redirecting here would bounce
// healthy internal traffic to a port nothing is listening on.
if (!app.Environment.IsDevelopment() && !IsRunningInContainer())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors("Default");

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();

app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("EnableApiDocs"))
{
    app.MapOpenApi();

    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("Backend Application - Tasks API")
            .WithTheme(ScalarTheme.BluePlanet)
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);

        foreach (var documentName in ApiVersions.DocumentNames)
        {
            options.AddDocument(documentName, $"Version {documentName[1..]}.0", $"/openapi/{documentName}.json");
        }
    });

    app.MapGet("/", () => Results.Redirect("/scalar"))
       .ExcludeFromDescription();
}

app.MapControllers();

app.MapApplicationHealthChecks();

await app.MigrateAndSeedAsync();

app.Logger.LogInformation(
    "Backend Application Tasks API started | Environment={Environment} | Docs=/scalar | Health=/health/ready",
    app.Environment.EnvironmentName);

await app.RunAsync();

static bool IsRunningInContainer() =>
    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";

/// <summary>
/// Exposed so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
