using System.Text.Json.Serialization;

namespace BackendApplication.DTOs.Common;

/// <summary>
/// One page of results plus the metadata a client needs to navigate the rest.
/// </summary>
/// <typeparam name="T">The item type on this page.</typeparam>
/// <remarks>
/// Uses offset pagination (<c>LIMIT</c> / <c>OFFSET</c>), which is simple and lets a
/// client jump to any page. The trade-off is that a deep OFFSET makes PostgreSQL walk
/// every skipped row, and that rows inserted between requests shift the window.
/// </remarks>
public sealed class PagedResult<T>
{
    /// <summary>The items on this page.</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>1-based page index.</summary>
    public int PageNumber { get; init; }

    /// <summary>Number of items requested per page.</summary>
    public int PageSize { get; init; }

    /// <summary>Total matching rows across all pages.</summary>
    public int TotalCount { get; init; }

    /// <summary>Total number of pages. Computed, not stored.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>True when a previous page exists.</summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>True when a next page exists.</summary>
    public bool HasNextPage => PageNumber < TotalPages;

    /// <summary>HATEOAS-style navigation links, populated by the controller.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PageLinks? Links { get; set; }

    /// <summary>Factory used by the service layer after it has run the two queries.</summary>
    public static PagedResult<T> Create(IReadOnlyList<T> items, int pageNumber, int pageSize, int totalCount)
        => new()
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount
        };

    /// <summary>Projects the items to a different type while keeping the page metadata.</summary>
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector)
        => new()
        {
            Items = Items.Select(selector).ToList(),
            PageNumber = PageNumber,
            PageSize = PageSize,
            TotalCount = TotalCount,
            Links = Links
        };
}

/// <summary>Absolute URLs for paging through a result set.</summary>
/// <param name="Self">This page.</param>
/// <param name="First">The first page.</param>
/// <param name="Previous">The previous page, or null on page 1.</param>
/// <param name="Next">The next page, or null on the last page.</param>
/// <param name="Last">The final page.</param>
public sealed record PageLinks(
    string Self,
    string First,
    string? Previous,
    string? Next,
    string Last);
