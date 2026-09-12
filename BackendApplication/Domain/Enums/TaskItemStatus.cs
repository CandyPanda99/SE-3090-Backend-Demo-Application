namespace BackendApplication.Domain.Enums;

/// <summary>
/// Where a task sits in its lifecycle.
/// </summary>
/// <remarks>
/// Named <c>TaskItemStatus</c> rather than the obvious <c>TaskStatus</c> on purpose:
/// <see cref="System.Threading.Tasks.TaskStatus"/> already exists in the base class
/// library and is in scope everywhere thanks to implicit usings. Two types with the
/// same short name force every file to disambiguate, so the clash is worth avoiding
/// once here instead of apologising for it in forty files.
///
/// Persisted as a string column (see <c>Data/Configurations/TaskItemConfiguration.cs</c>)
/// and serialised as a string in JSON, so reordering this enum can never silently
/// change what existing rows mean.
/// </remarks>
public enum TaskItemStatus
{
    /// <summary>Captured in the backlog, nobody has started it.</summary>
    Todo = 0,

    /// <summary>Actively being worked on. Requires an assignee.</summary>
    InProgress = 1,

    /// <summary>Started, but waiting on something outside the team's control.</summary>
    Blocked = 2,

    /// <summary>Finished. Terminal - a done task cannot move again.</summary>
    Done = 3,

    /// <summary>Abandoned. Terminal, and kept for the record rather than deleted.</summary>
    Cancelled = 4
}

/// <summary>
/// The legal moves between <see cref="TaskItemStatus"/> values.
/// </summary>
/// <remarks>
/// A state machine written down in one place beats the same rules scattered across
/// service methods as <c>if</c> statements. Everything the domain permits is visible
/// in <see cref="AllowedTransitions"/>, so adding a state means editing one table
/// rather than hunting for every branch that needs a new case.
/// </remarks>
public static class TaskStatusTransitions
{
    /// <summary>Which statuses each status may move to. Missing key = terminal state.</summary>
    private static readonly Dictionary<TaskItemStatus, TaskItemStatus[]> AllowedTransitions = new()
    {
        [TaskItemStatus.Todo] = [TaskItemStatus.InProgress, TaskItemStatus.Cancelled],
        [TaskItemStatus.InProgress] = [TaskItemStatus.Blocked, TaskItemStatus.Done, TaskItemStatus.Cancelled],
        [TaskItemStatus.Blocked] = [TaskItemStatus.InProgress, TaskItemStatus.Cancelled],

        // Done and Cancelled are deliberately absent: they are terminal.
        [TaskItemStatus.Done] = [],
        [TaskItemStatus.Cancelled] = []
    };

    /// <summary>True when <paramref name="to"/> is a legal next state after <paramref name="from"/>.</summary>
    /// <remarks>Moving to the state you are already in counts as legal, and is a no-op.</remarks>
    public static bool CanMove(TaskItemStatus from, TaskItemStatus to)
        => from == to || (AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to));

    /// <summary>The legal next states, for building a helpful error message.</summary>
    public static IReadOnlyList<TaskItemStatus> NextStatesFrom(TaskItemStatus from)
        => AllowedTransitions.TryGetValue(from, out var allowed) ? allowed : [];

    /// <summary>True when no further transition is possible.</summary>
    public static bool IsTerminal(TaskItemStatus status) => NextStatesFrom(status).Count == 0;
}
