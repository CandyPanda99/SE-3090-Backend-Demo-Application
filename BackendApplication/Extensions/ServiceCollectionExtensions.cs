using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using BackendApplication.Configuration;
using BackendApplication.Data;
using BackendApplication.Data.Interceptors;
using BackendApplication.Data.Repositories;
using BackendApplication.Domain.Enums;
using BackendApplication.Filters;
using BackendApplication.Services.Abstractions;
using BackendApplication.Services.Implementations;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace BackendApplication.Extensions;

/// <summary>
/// Groups the application's dependency-injection registrations into readable,
/// single-purpose extension methods.
/// </summary>
/// <remarks>
/// Every method below follows the same three rules, so once you understand one you
/// understand all of them:
///
/// 1. The class is <c>static</c> and each method's first parameter is written
///    <c>this IServiceCollection services</c>. That <c>this</c> keyword makes it an
///    "extension method": C# lets you call it as if it were built into IServiceCollection,
///    so <c>ServiceCollectionExtensions.AddPersistence(services, config)</c> can instead be
///    written <c>services.AddPersistence(config)</c>.
///
/// 2. Each method only *records* what the app will need later. It says "when someone asks
///    for ITaskService, give them a TaskService" - it does not create anything yet. The
///    objects get built after <c>builder.Build()</c>, when a request asks for them.
///
/// 3. Each method ends with <c>return services;</c> - handing back the very same
///    collection it was given. That is the only reason the calls in Program.cs can be
///    chained as <c>.AddA().AddB().AddC()</c>.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Binds and validates the strongly-typed options classes.
    /// </summary>
    /// <remarks>
    /// This is the "options pattern". Reading a setting directly looks like
    /// <c>configuration["Database:MaxPoolSize"]</c>, which hands you the string "50" that
    /// you then convert to a number in every class that needs it - and that magic string
    /// is invisible to the compiler, so a typo yields null at runtime rather than a build
    /// error.
    ///
    /// Instead each section is copied once into an ordinary C# class. A class that needs a
    /// setting asks for <c>IOptions&lt;DatabaseOptions&gt;</c> and reads
    /// <c>options.Value.MaxPoolSize</c>: a real int, autocompleted and compiler-checked.
    ///
    /// <c>ValidateOnStart</c> is the important line. Without it the [Required] and [Range]
    /// attributes are checked lazily, so a bad value surfaces mid-request instead of as an
    /// immediate startup failure naming the property that is wrong.
    /// </remarks>
    public static IServiceCollection AddApplicationOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers EF Core, the PostgreSQL data source and both connection pools.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var dbOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
                        ?? throw new InvalidOperationException(
                            "The 'Database' configuration section is missing. Check appsettings.json.");

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(dbOptions.BuildConnectionString());

        dataSourceBuilder.EnableDynamicJson();

        var dataSource = dataSourceBuilder.Build();

        services.AddSingleton(dataSource);

        // Singletons, because a pooled DbContext shares one DbContextOptions instance and
        // EF Core resolves interceptors from the root provider. Registering these as scoped
        // would throw at startup.
        services.AddSingleton<AuditingSaveChangesInterceptor>();
        services.AddSingleton<SlowQueryLoggingInterceptor>();

        // AddDbContextPool rather than AddDbContext: building a context is not free, so the
        // pool resets and reuses instances instead of discarding them. The catch is that
        // per-request state must never be stored in a field on the context, because the
        // next request would inherit it.
        services.AddDbContextPool<TasksDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(dataSource, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(TasksDbContext).Assembly.FullName);

                // Retries only errors known to be transient - a network blip or a failover -
                // with growing backoff. A genuine SQL mistake still fails immediately.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: dbOptions.MaxRetryCount,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);

                npgsql.CommandTimeout(dbOptions.CommandTimeoutSeconds);
            });

            options.AddInterceptors(
                serviceProvider.GetRequiredService<AuditingSaveChangesInterceptor>(),
                serviceProvider.GetRequiredService<SlowQueryLoggingInterceptor>());

            options.EnableSensitiveDataLogging(dbOptions.EnableSensitiveDataLogging);
            options.EnableDetailedErrors(dbOptions.EnableDetailedErrors);

            // An API mostly reads and serialises. Change tracking is wasted work there, so
            // it is off by default and the handful of methods that update something opt
            // back in with .AsTracking().
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

            // The soft-delete query filter plus a required relationship makes EF warn that a
            // row could be hidden because its parent was filtered out. That is exactly the
            // intended behaviour here, so silence it rather than see it on every startup.
            options.ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId
                    .PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
        },
        poolSize: dbOptions.DbContextPoolSize);

        // All repositories share the one scoped TasksDbContext, so SaveChangesAsync on any
        // of them commits the whole request atomically.
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        return services;
    }

    /// <summary>Registers the business services and their supporting abstractions.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        // TimeProvider instead of DateTime.UtcNow scattered through the code: a test can
        // substitute a fake clock and assert on due dates and token expiry.
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<ICurrentUserService, CurrentUserService>();

        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IAuthService, AuthService>();

        services.AddSingleton<ITokenService, TokenService>();

        // Scoped, because the task validators depend on repositories, which are scoped.
        services.AddValidatorsFromAssemblyContaining<Validation.CreateTaskDtoValidator>(
            ServiceLifetime.Scoped);

        return services;
    }

    /// <summary>Configures JWT bearer authentication and the authorisation policies.</summary>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                  ?? throw new InvalidOperationException("The 'Jwt' configuration section is missing.");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,

                ValidateAudience = true,
                ValidAudience = jwt.Audience,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),

                ValidateLifetime = true,

                // Defaults to five minutes in this library, which quietly extends every
                // token past its stated expiry. Configured explicitly, and to zero.
                ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),

                NameClaimType = System.Security.Claims.ClaimTypes.Email,
                RoleClaimType = System.Security.Claims.ClaimTypes.Role
            };

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("JwtBearer");

                    // An expired token is routine and gets a hint header so the client knows
                    // to refresh rather than re-prompt for a password. Anything else is
                    // worth a warning.
                    if (context.Exception is SecurityTokenExpiredException)
                    {
                        context.Response.Headers["X-Token-Expired"] = "true";
                        logger.LogInformation("Rejected an expired token.");
                    }
                    else
                    {
                        logger.LogWarning("Token validation failed: {Reason}", context.Exception.Message);
                    }

                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("LeadOrAdmin", policy =>
                policy.RequireRole(Roles.Lead, Roles.Admin));

            options.AddPolicy("AdminOnly", policy =>
                policy.RequireRole(Roles.Admin));

            options.AddPolicy("VerifiedUser", policy =>
                policy.RequireAuthenticatedUser()
                      .RequireClaim(System.Security.Claims.ClaimTypes.Email));
        });

        return services;
    }

    /// <summary>Enables URL-segment API versioning and teaches the API explorer about it.</summary>
    /// <remarks>
    /// Only v1 exists today. The machinery is here anyway because retrofitting versioning
    /// means changing routes clients already call, whereas adding a version to an API that
    /// already has one is routine.
    /// </remarks>
    public static IServiceCollection AddApiVersioningSupport(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);

            options.AssumeDefaultVersionWhenUnspecified = true;

            options.ReportApiVersions = true;

            // The URL segment is the primary source; the rest are fallbacks for clients
            // that prefer a header or a query parameter.
            options.ApiVersionReader = ApiVersionReader.Combine(
                new UrlSegmentApiVersionReader(),
                new HeaderApiVersionReader("X-Api-Version"),
                new QueryStringApiVersionReader("api-version"),
                new MediaTypeApiVersionReader("v"));
        })
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";

            options.SubstituteApiVersionInUrl = true;
        });

        return services;
    }

    /// <summary>Registers one OpenAPI document per API version.</summary>
    public static IServiceCollection AddOpenApiDocuments(this IServiceCollection services)
    {
        foreach (var documentName in ApiVersions.DocumentNames)
        {
            services.AddOpenApi(documentName, options =>
            {
                options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
                options.AddDocumentTransformer<ApiDocumentInfoTransformer>();
                options.AddOperationTransformer<AuthorizationOperationTransformer>();

                options.ShouldInclude = description => description.GroupName == documentName;
            });
        }

        return services;
    }

    /// <summary>Configures rate limiting to protect sensitive endpoints.</summary>
    /// <remarks>
    /// Two layers. A global token bucket smooths bursts from any one caller, and a much
    /// stricter fixed window on the auth endpoints makes password guessing impractical.
    /// The partition key is the username when authenticated and the IP when not, so one
    /// noisy anonymous client cannot exhaust everybody else's budget.
    /// </remarks>
    public static IServiceCollection AddRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddFixedWindowLimiter("auth", limiter =>
            {
                limiter.PermitLimit = 10;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                limiter.QueueLimit = 0;
            });

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var partitionKey = context.User.Identity?.IsAuthenticated == true
                    ? context.User.Identity.Name ?? "authenticated"
                    : context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

                return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ =>
                    new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 100,
                        TokensPerPeriod = 50,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";

                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    type = "https://httpstatuses.io/429",
                    title = "Too many requests",
                    status = 429,
                    detail = "Rate limit exceeded. Please retry after 60 seconds.",
                    errorCode = "rate_limit_exceeded"
                }, cancellationToken);
            };
        });

        return services;
    }

    /// <summary>Registers CORS policies for browser-based clients.</summary>
    /// <remarks>
    /// Named origins rather than AllowAnyOrigin, because AllowCredentials and a wildcard
    /// are mutually exclusive - and a wildcard on a credentialed API is the bug that lets
    /// any site read a logged-in user's data.
    /// </remarks>
    public static IServiceCollection AddCorsPolicies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                             ?? ["http://localhost:3000", "http://localhost:5173"];

        services.AddCors(options =>
        {
            options.AddPolicy("Default", policy =>
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      // Custom headers are invisible to browser JavaScript unless exposed.
                      .WithExposedHeaders("X-Pagination", "X-Correlation-ID",
                                          "api-supported-versions", "api-deprecated-versions")
                      .AllowCredentials());
        });

        return services;
    }

    /// <summary>Registers MVC controllers along with the global filters and JSON settings.</summary>
    public static IServiceCollection AddApiControllers(this IServiceCollection services)
    {
        services.AddControllers(options =>
        {
            options.Filters.Add<ValidationFilter>();
            options.Filters.Add<PaginationHeaderFilter>();

            options.ReturnHttpNotAcceptable = true;
        })
        .AddJsonOptions(options =>
        {
            // Enums as "InProgress", not 1. A numeric enum in JSON is unreadable and breaks
            // silently the day somebody reorders the C# declaration.
            options.JsonSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter());

            options.JsonSerializerOptions.PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.CamelCase;

            options.JsonSerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;

            // Reject unknown fields instead of ignoring them. A client that misspells
            // "priority" should be told, not have its value silently dropped.
            options.JsonSerializerOptions.UnmappedMemberHandling =
                System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
        });

        // Turn off the automatic 400 for invalid model state, so ValidationFilter can
        // produce the one 422 shape this API uses everywhere.
        services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
        {
            options.SuppressModelStateInvalidFilter = true;
        });

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance =
                    $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";

                context.ProblemDetails.Extensions["correlationId"] =
                    context.HttpContext.Items[Middleware.CorrelationIdMiddleware.ItemKey]?.ToString()
                    ?? context.HttpContext.TraceIdentifier;
            };
        });

        return services;
    }

    /// <summary>Registers liveness and readiness health checks.</summary>
    /// <remarks>
    /// The distinction matters to an orchestrator: liveness failing means restart me,
    /// readiness failing means stop sending traffic. Only readiness touches the database -
    /// restarting the API would not fix a database outage.
    /// </remarks>
    public static IServiceCollection AddApplicationHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var dbOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>();

        services.AddHealthChecks()
            .AddNpgSql(
                connectionString: dbOptions?.BuildConnectionString() ?? string.Empty,
                healthQuery: "SELECT 1;",
                name: "postgresql",
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                tags: ["ready", "db"]);

        return services;
    }
}
