using System.Text.Json;
using BackendApplication.DTOs.Common;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BackendApplication.Filters;

/// <summary>
/// When an action returns a <see cref="PagedResult{T}"/>, copies the paging metadata into
/// an <c>X-Pagination</c> response header.
/// </summary>
/// <remarks>
/// The same numbers are already in the body. The header exists so a client can read the
/// totals from a HEAD request, or without parsing a large payload.
/// </remarks>
public sealed class PaginationHeaderFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: not null } objectResult)
        {
            var value = objectResult.Value;

            // The payload is usually wrapped in ApiResponse<T>, so unwrap Data first.
            var payload = value.GetType().IsGenericType &&
                          value.GetType().GetGenericTypeDefinition() == typeof(ApiResponse<>)
                ? value.GetType().GetProperty(nameof(ApiResponse<object>.Data))?.GetValue(value)
                : value;

            if (payload is not null && IsPagedResult(payload.GetType()))
            {
                var type = payload.GetType();

                var meta = new
                {
                    PageNumber = type.GetProperty("PageNumber")!.GetValue(payload),
                    PageSize = type.GetProperty("PageSize")!.GetValue(payload),
                    TotalCount = type.GetProperty("TotalCount")!.GetValue(payload),
                    TotalPages = type.GetProperty("TotalPages")!.GetValue(payload),
                    HasNextPage = type.GetProperty("HasNextPage")!.GetValue(payload),
                    HasPreviousPage = type.GetProperty("HasPreviousPage")!.GetValue(payload)
                };

                context.HttpContext.Response.Headers["X-Pagination"] =
                    JsonSerializer.Serialize(meta, JsonOptions);
            }
        }

        await next();
    }

    private static bool IsPagedResult(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PagedResult<>);

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
