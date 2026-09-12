using System.ComponentModel.DataAnnotations;
using BackendApplication.Domain.Enums;
using BackendApplication.DTOs.Common;

namespace BackendApplication.DTOs.Tasks;

/// <summary>
/// Everything a client can put in the query string of <c>GET /api/v1/tasks</c>.
/// </summary>
/// <remarks>
/// Inherits paging and sorting from <see cref="PaginationQuery"/> and adds filtering
/// and searching.
///
/// Example:
/// <c>
/// GET /api/v1/tasks?search=pagination&amp;status=InProgress&amp;priority=High
///     &amp;assigneeId=3&amp;overdueOnly=true&amp;sortBy=dueat&amp;sortDirection=asc
///     &amp;pageNumber=1&amp;pageSize=20
/// </c>
/// </remarks>
public sealed class TaskQueryParameters : PaginationQuery
{
    /// <summary>Free-text search across reference, title and description (case-insensitive).</summary>
    /// <example>pagination</example>
    [StringLength(100)]
    public string? Search { get; set; }

    /// <summary>Restrict to one lifecycle status.</summary>
    /// <example>InProgress</example>
    public TaskItemStatus? Status { get; set; }

    /// <summary>Restrict to one priority.</summary>
    /// <example>High</example>
    public TaskPriority? Priority { get; set; }

    /// <summary>Restrict to tasks owned by one user.</summary>
    /// <example>3</example>
    public int? AssigneeId { get; set; }

    /// <summary>When true, returns only tasks that nobody owns yet.</summary>
    /// <remarks>Ignored when <see cref="AssigneeId"/> is also supplied - the two contradict.</remarks>
    public bool? UnassignedOnly { get; set; }

    /// <summary>When true, returns only open tasks whose due date has passed.</summary>
    public bool? OverdueOnly { get; set; }

    /// <summary>When true, hides Done and Cancelled tasks.</summary>
    public bool? OpenOnly { get; set; }

    /// <summary>Inclusive lower bound on the due date.</summary>
    public DateTime? DueAfterUtc { get; set; }

    /// <summary>Inclusive upper bound on the due date.</summary>
    public DateTime? DueBeforeUtc { get; set; }

    /// <summary>
    /// Allow-list of sortable fields, used by the service to build a safe ORDER BY.
    /// </summary>
    public static readonly string[] AllowedSortFields =
        ["reference", "title", "status", "priority", "dueat", "createdat"];
}
