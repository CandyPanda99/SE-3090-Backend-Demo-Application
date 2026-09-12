using BackendApplication.Domain.Enums;

namespace BackendApplication.Tests.Domain;

/// <summary>
/// A representative sample of the state machine in <see cref="TaskStatusTransitions"/>.
/// </summary>
/// <remarks>
/// Not an exhaustive transition table - just enough to prove the allow-list is read the
/// right way round and that terminal states really are terminal.
/// </remarks>
public class TaskStatusTransitionsTests
{
    [Theory]
    [InlineData(TaskItemStatus.Todo, TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.InProgress, TaskItemStatus.Done)]
    public void CanMove_allows_a_documented_transition(TaskItemStatus from, TaskItemStatus to)
        => Assert.True(TaskStatusTransitions.CanMove(from, to));

    [Theory]
    [InlineData(TaskItemStatus.Todo, TaskItemStatus.Done)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.InProgress)]
    public void CanMove_rejects_a_transition_that_is_not_on_the_list(TaskItemStatus from, TaskItemStatus to)
        => Assert.False(TaskStatusTransitions.CanMove(from, to));

    [Fact]
    public void CanMove_treats_a_move_to_the_current_state_as_legal()
        => Assert.True(TaskStatusTransitions.CanMove(TaskItemStatus.Done, TaskItemStatus.Done));

    [Fact]
    public void Done_is_terminal()
    {
        Assert.True(TaskStatusTransitions.IsTerminal(TaskItemStatus.Done));
        Assert.Empty(TaskStatusTransitions.NextStatesFrom(TaskItemStatus.Done));
    }

    [Fact]
    public void NextStatesFrom_lists_the_legal_moves()
        => Assert.Equal(
            [TaskItemStatus.Blocked, TaskItemStatus.Done, TaskItemStatus.Cancelled],
            TaskStatusTransitions.NextStatesFrom(TaskItemStatus.InProgress));
}
