using System.Text.Json;
using BackendApplication.Configuration;
using BackendApplication.Data;
using BackendApplication.Data.Seed;
using BackendApplication.Middleware;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BackendApplication.Extensions;

/// <summary>
/// Helpers for composing the HTTP pipeline and for startup chores.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>Adds the global exception handler. Must be registered first.</summary>
    /// <remarks>
    /// Order is not cosmetic here: middleware wraps everything registered after it, so a
    /// handler added halfway down the pipeline cannot catch what happened above it.
    /// </remarks>
    public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder app)
        => app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

    /// <summary>Assigns and echoes an <c>X-Correlation-ID</c> for every request.</summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();

    /// <summary>Logs one structured line per request with its status and duration.</summary>
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
        => app.UseMiddleware<RequestLoggingMiddleware>();

    /// <summary>
    /// Applies pending EF Core migrations and seeds demo data, if configuration allows.
    /// </summary>
    /// <remarks>
    /// Both flags are on in Development and Docker and off in Production, where schema
    /// changes belong in a deployment step rather than in whichever instance happens to
    /// start first.
    /// </remarks>
    public static async Task<WebApplication> MigrateAndSeedAsync(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        if (!options.ApplyMigrationsOnStartup && !options.SeedDemoData)
        {
            return app;
        }

        // A scope of its own: the DbContext is scoped, and there is no request scope yet.
        using var scope = app.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            if (options.ApplyMigrationsOnStartup)
            {
                logger.LogInformation("Applying database migrations...");

                await db.Database.MigrateAsync();

                logger.LogInformation("Migrations applied.");
            }

            if (options.SeedDemoData)
            {
                await DbSeeder.SeedAsync(db, logger);
            }
        }
        catch (Exception ex)
        {
            // Rethrow after logging. An API that cannot reach its database should fail to
            // start, loudly, rather than come up and return 500 to every caller.
            logger.LogCritical(ex, "Database initialisation failed. The API cannot serve requests.");

            throw;
        }

        return app;
    }

    /// <summary>
    /// Maps <c>/health/live</c> and <c>/health/ready</c> with a readable JSON body.
    /// </summary>
    public static WebApplication MapApplicationHealthChecks(this WebApplication app)
    {
        // Predicate => false means "run no checks": the process answering at all is the
        // liveness signal.
        app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthResponseAsync
        }).AllowAnonymous();

        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthResponseAsync
        }).AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Writes a structured health-check body naming each individual check.
    /// </summary>
    /// <remarks>
    /// The default writer returns the single word "Healthy". Naming each check turns a
    /// failing probe into a diagnosis instead of a starting point for one.
    /// </remarks>
    private static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = entry.Value.Duration.TotalMilliseconds,
                description = entry.Value.Description,

                // The exception message can name hosts and credentials, so it is shown in
                // Development only.
                error = context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()
                    ? entry.Value.Exception?.Message
                    : null
            })
        };

        return context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            }));
    }
}
