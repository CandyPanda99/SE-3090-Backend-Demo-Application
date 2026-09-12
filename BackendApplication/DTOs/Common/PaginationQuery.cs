using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using BackendApplication.Domain.Enums;

namespace BackendApplication.DTOs.Common;

/// <summary>
/// Base class for query-string parameters on any paginated list endpoint.
/// </summary>
public class PaginationQuery
{
    /// <summary>Hard server-side ceiling on page size. Larger values are clamped.</summary>
    /// <remarks>
    /// Clamped rather than rejected: a client asking for 10,000 rows gets 100 and a
    /// working response, instead of a 400 it has to learn to handle. The ceiling exists
    /// so one request cannot ask the database for the whole table.
    /// </remarks>
    public const int MaxPageSize = 100;

    private int _pageSize = 20;
    private int _pageNumber = 1;

    /// <summary>1-based page index. Values below 1 are coerced to 1.</summary>
    /// <example>1</example>
    [Range(1, int.MaxValue, ErrorMessage = "pageNumber must be 1 or greater.")]
    [DefaultValue(1)]
    public int PageNumber
    {
        get => _pageNumber;
        set => _pageNumber = value < 1 ? 1 : value;
    }

    /// <summary>Items per page. Clamped to <see cref="MaxPageSize"/>.</summary>
    /// <example>20</example>
    [Range(1, MaxPageSize, ErrorMessage = "pageSize must be between 1 and 100.")]
    [DefaultValue(20)]
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => 1,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    /// <summary>Property name to sort by, e.g. <c>title</c>, <c>dueat</c>, <c>priority</c>.</summary>
    /// <remarks>
    /// Mapped through a hard-coded allow-list of LINQ expressions in the service; the raw
    /// string never reaches SQL. Interpolating it into an ORDER BY would be a SQL
    /// injection hole that no amount of parameterising elsewhere would close.
    /// </remarks>
    /// <example>dueat</example>
    public string? SortBy { get; set; }

    /// <summary>Ascending or descending.</summary>
    /// <example>Asc</example>
    [DefaultValue(SortDirection.Asc)]
    public SortDirection SortDirection { get; set; } = SortDirection.Asc;

    /// <summary>Rows to skip (SQL OFFSET).</summary>
    public int Skip => (PageNumber - 1) * PageSize;

    /// <summary>Rows to take (SQL LIMIT).</summary>
    public int Take => PageSize;
}
