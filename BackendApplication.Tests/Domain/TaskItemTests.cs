using BackendApplication.Domain.Entities;
using BackendApplication.Domain.Enums;

namespace BackendApplication.Tests.Domain;

/// <summary>
/// A sample of the behaviour <see cref="TaskItem"/> owns itself.
/// </summary>
/// <remarks>
/// <c>TryTransitionTo</c> takes the current time as a parameter precisely so a test can
/// pin it, so the assertion about <c>CompletedAtUtc</c> is exact rather than "roughly now".
/// </remarks>
public class TaskItemTests
{
    private static readonly DateTime Now = new(2026, 9, 12, 10, 30, 0, DateTimeKind.Utc);

    private static TaskItem TaskIn(TaskItemStatus status) =>
        new() { Reference = "TSK-0001", Title = "Write the tests", Status = status };

    [Fact]
    public void TryTransitionTo_applies_a_legal_move()
    {
        var task = TaskIn(TaskItemStatus.Todo);

        Assert.True(task.TryTransitionTo(TaskItemStatus.InProgress, Now));
        Assert.Equal(TaskItemStatus.InProgress, task.Status);
    }

    [Fact]
    public void TryTransitionTo_leaves_the_task_untouched_on_an_illegal_move()
    {
        var task = TaskIn(TaskItemStatus.Todo);

        Assert.False(task.TryTransitionTo(TaskItemStatus.Done, Now));
        Assert.Equal(TaskItemStatus.Todo, task.Status);
        Assert.Null(task.CompletedAtUtc);
    }

    [Fact]
    public void Reaching_Done_stamps_the_completion_time()
    {
        var task = TaskIn(TaskItemStatus.InProgress);

        Assert.True(task.TryTransitionTo(TaskItemStatus.Done, Now));
        Assert.Equal(Now, task.CompletedAtUtc);
    }

    [Fact]
    public void A_past_due_date_on_an_unfinished_task_is_overdue()
    {
        var task = TaskIn(TaskItemStatus.InProgress);
        task.DueAtUtc = DateTime.UtcNow.AddDays(-1);

        Assert.True(task.IsOverdue);
        Assert.True(task.IsOpen);
    }

    [Fact]
    public void A_closed_task_is_never_overdue()
    {
        var task = TaskIn(TaskItemStatus.Done);
        task.DueAtUtc = DateTime.UtcNow.AddDays(-1);

        Assert.False(task.IsOverdue);
        Assert.False(task.IsOpen);
    }
}
