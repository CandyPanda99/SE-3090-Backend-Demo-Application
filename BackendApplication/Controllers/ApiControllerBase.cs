using Asp.Versioning;
using BackendApplication.DTOs.Common;
using BackendApplication.Middleware;
using Microsoft.AspNetCore.Http.Extensions;

namespace BackendApplication.Controllers;

/// <summary>
/// Shared base class for every controller in this API.
/// </summary>
/// <remarks>
/// Holds the things every controller would otherwise repeat: the response envelope, the
/// correlation id, and pagination link building. The <c>[ProducesResponseType]</c>
/// attributes here apply to every derived action, so the two error shapes every endpoint
/// can return are documented once.
/// </remarks>
[ApiController]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The correlation id for the current request (set by the middleware).</summary>
    protected string CorrelationId =>
        HttpContext.Items[CorrelationIdMiddleware.ItemKey]?.ToString() ?? HttpContext.TraceIdentifier;

    /// <summary>The API version that matched this request, e.g. "1.0".</summary>
    protected string? RequestedApiVersion =>
        HttpContext.Features.Get<IApiVersioningFeature>()?.RequestedApiVersion?.ToString();

    /// <summary>Wraps a payload in the standard success envelope, filling in the metadata.</summary>
    protected ApiResponse<T> Envelope<T>(T data, string? message = null) => new()
    {
        Data = data,
        Message = message,
        Meta = new ResponseMeta
        {
            CorrelationId = CorrelationId,
            ApiVersion = RequestedApiVersion
        }
    };

    /// <summary>
    /// Adds HATEOAS navigation links to a paged result, preserving every filter the caller
    /// supplied.
    /// </summary>
    /// <remarks>
    /// The client follows <c>next</c> rather than computing page numbers and re-attaching
    /// its filters, which is where off-by-one paging bugs usually come from.
    /// </remarks>
    protected void AddPaginationLinks<T>(PagedResult<T> page)
    {
        page.Links = new PageLinks(
            Self: BuildPageUrl(page.PageNumber),
            First: BuildPageUrl(1),
            Previous: page.HasPreviousPage ? BuildPageUrl(page.PageNumber - 1) : null,
            Next: page.HasNextPage ? BuildPageUrl(page.PageNumber + 1) : null,
            Last: BuildPageUrl(Math.Max(page.TotalPages, 1)));
    }

    private string BuildPageUrl(int pageNumber)
    {
        // Copy every query parameter except the page number, then put the new one back, so
        // filters and sorting survive the jump to another page.
        var parameters = Request.Query
            .Where(kv => !string.Equals(kv.Key, "pageNumber", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

        parameters["pageNumber"] = pageNumber.ToString();

        var query = QueryString.Create(parameters!);

        return UriHelper.BuildAbsolute(
            Request.Scheme, Request.Host, Request.PathBase, Request.Path, query);
    }
}
