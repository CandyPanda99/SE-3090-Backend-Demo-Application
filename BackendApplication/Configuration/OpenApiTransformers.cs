using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BackendApplication.Configuration;

/// <summary>
/// Adds the JWT bearer security scheme to the generated OpenAPI document, so the
/// documentation UI shows an "Authorize" button.
/// </summary>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    /// <summary>The name we register the scheme under; referenced by each operation.</summary>
    public const string SchemeName = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var scheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Name = "Authorization",
            Description =
                "JWT access token obtained from POST /api/v1/auth/login.\n\n" +
                "Paste ONLY the token - the UI adds the 'Bearer ' prefix for you.\n\n" +
                "Demo accounts: admin@tasks.local / Admin#12345, " +
                "lead@tasks.local / Lead#12345, member@tasks.local / Member#12345"
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = scheme;

        return Task.CompletedTask;
    }
}

/// <summary>
/// Marks each operation that requires authentication, so the docs show a padlock.
/// </summary>
/// <remarks>
/// Reads the endpoint metadata rather than a hand-maintained list, so an endpoint
/// cannot be documented as public while actually requiring a token.
/// </remarks>
public sealed class AuthorizationOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        var requiresAuth = metadata.OfType<IAuthorizeData>().Any()
                           && !metadata.OfType<IAllowAnonymous>().Any();

        if (!requiresAuth)
        {
            return Task.CompletedTask;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerSecuritySchemeTransformer.SchemeName)] = []
            }
        ];

        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid bearer token." });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Authenticated, but not permitted." });

        return Task.CompletedTask;
    }
}

/// <summary>
/// Fills in the title, description and contact shown at the top of the documentation page.
/// </summary>
public sealed class ApiDocumentInfoTransformer : IOpenApiDocumentTransformer
{
    private readonly IApiVersionDescriptionProvider _provider;

    public ApiDocumentInfoTransformer(IApiVersionDescriptionProvider provider) => _provider = provider;

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var description = _provider.ApiVersionDescriptions
            .FirstOrDefault(d => d.GroupName == context.DocumentName);

        document.Info = new OpenApiInfo
        {
            Title = "Backend Application - Tasks API",
            Version = description?.ApiVersion.ToString() ?? context.DocumentName,
            Description = Description,
            Contact = new OpenApiContact
            {
                Name = "SLIIT - ASP.NET Core Lecture",
                Url = new Uri("https://github.com/")
            },
            License = new OpenApiLicense
            {
                Name = "MIT",
                Url = new Uri("https://opensource.org/licenses/MIT")
            }
        };

        return Task.CompletedTask;
    }

    private const string Description = """
        A teaching API for an ASP.NET Core lecture, built around a single resource: tasks
        on a team board. One resource, but every layer - domain, persistence, DTOs,
        services, validation, error handling and authorisation - so each layer's job is
        visible without four interlocking resources obscuring it.

        **Demo accounts** (seeded automatically):

        | Email | Password | Role |
        |---|---|---|
        | `admin@tasks.local` | `Admin#12345` | Admin |
        | `lead@tasks.local` | `Lead#12345` | Lead |
        | `member@tasks.local` | `Member#12345` | Member |

        **How to authenticate:** call `POST /api/v1/auth/login`, copy the `accessToken`
        from the response, then click **Authorize** at the top of this page and paste it.

        ### The status state machine

        Tasks move through `Todo -> InProgress -> Done`, with `Blocked` as a detour and
        `Cancelled` reachable from any open state. `Done` and `Cancelled` are terminal.
        An illegal move returns **409 Conflict** naming the states that *are* reachable.

        | From | Allowed next |
        |---|---|
        | `Todo` | `InProgress`, `Cancelled` |
        | `InProgress` | `Blocked`, `Done`, `Cancelled` |
        | `Blocked` | `InProgress`, `Cancelled` |
        | `Done` | *(terminal)* |
        | `Cancelled` | *(terminal)* |

        Moving a task to `InProgress` additionally requires an assignee - nobody works on
        a task that belongs to no one.

        **Concepts demonstrated:** controllers, services, DTOs, EF Core with PostgreSQL,
        the repository pattern, middleware, filters, global exception handling with RFC
        9457 ProblemDetails, JWT authentication with role and policy authorisation,
        URL-segment API versioning, offset pagination with HATEOAS links,
        FluentValidation alongside DataAnnotations, optimistic concurrency, soft delete,
        connection pooling, rate limiting and health checks.
        """;
}
