namespace BackendApplication.Domain.Enums;

/// <summary>
/// How urgent a task is. Drives default ordering of the backlog.
/// </summary>
/// <remarks>
/// The numeric values are deliberately ascending by urgency, so
/// <c>ORDER BY "Priority" DESC</c> would put Critical first if the column were ever
/// stored numerically. It is stored as text instead - see the configuration class -
/// so the service sorts on an explicit CASE rather than relying on these numbers.
/// </remarks>
public enum TaskPriority
{
    /// <summary>Nice to have. No deadline pressure.</summary>
    Low = 0,

    /// <summary>The default for new tasks.</summary>
    Medium = 1,

    /// <summary>Should be picked up this cycle.</summary>
    High = 2,

    /// <summary>Drop everything.</summary>
    Critical = 3
}
