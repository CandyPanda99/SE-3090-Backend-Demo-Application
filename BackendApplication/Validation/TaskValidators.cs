using BackendApplication.Data.Repositories;
using BackendApplication.Domain.Entities;
using BackendApplication.DTOs.Tasks;
using FluentValidation;

namespace BackendApplication.Validation;

/// <summary>
/// FluentValidation rules for <see cref="CreateTaskDto"/>.
/// </summary>
/// <remarks>
/// The division of labour with DataAnnotations is deliberate. Annotations on the DTO
/// cover shape - required, length, range - and feed the OpenAPI schema, so the generated
/// documentation shows the constraints. These rules cover what annotations cannot:
/// conditions across two fields, and checks that need the database.
/// </remarks>
public sealed class CreateTaskDtoValidator : AbstractValidator<CreateTaskDto>
{
    public CreateTaskDtoValidator(IRepository<AppUser> users)
    {
        RuleFor(x => x.Title)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Title is required.")
            .Length(3, 200).WithMessage("Title must be between 3 and 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(4000);

        RuleFor(x => x.Priority)
            .IsInEnum().WithMessage("Priority must be one of: Low, Medium, High, Critical.");

        // Only checked when a due date was actually supplied - null means "no deadline",
        // which is legitimate and must not be reported as a validation error.
        RuleFor(x => x.DueAtUtc)
            .Must(BeInTheFuture)
            .When(x => x.DueAtUtc.HasValue)
            .WithMessage("Due date must be in the future.");

        RuleFor(x => x.EstimatedHours)
            .GreaterThan(0).WithMessage("Estimated hours must be greater than zero.")
            .LessThanOrEqualTo(9_999.99m)
            .PrecisionScale(6, 2, ignoreTrailingZeros: true)
                .WithMessage("Estimated hours may have at most 2 decimal places.")
            .When(x => x.EstimatedHours.HasValue);

        RuleFor(x => x.AssigneeId)
            .MustAsync(async (id, ct) => await users.ExistsAsync(u => u.Id == id!.Value, ct))
            .When(x => x.AssigneeId.HasValue)
            .WithMessage("User {PropertyValue} does not exist.")
            .WithErrorCode("assignee_not_found");
    }

    /// <summary>
    /// Compares against UTC now, whatever kind the incoming value carried.
    /// </summary>
    /// <remarks>
    /// A client may send a local time with an offset, an explicit Z, or no zone at all.
    /// Normalising here means the rule reads the same instant the database will store -
    /// see <c>TaskMappings.NormaliseToUtc</c>.
    /// </remarks>
    private static bool BeInTheFuture(DateTime? value)
    {
        if (value is not { } due)
        {
            return true;
        }

        var asUtc = due.Kind switch
        {
            DateTimeKind.Utc => due,
            DateTimeKind.Local => due.ToUniversalTime(),
            _ => DateTime.SpecifyKind(due, DateTimeKind.Utc)
        };

        return asUtc > DateTime.UtcNow;
    }
}

/// <summary>
/// FluentValidation rules for <see cref="UpdateTaskDto"/>.
/// </summary>
/// <remarks>
/// Note what is NOT repeated from the create validator: the due date is allowed to be in
/// the past here. A task whose deadline has already slipped still needs to be editable -
/// refusing the edit would trap it.
/// </remarks>
public sealed class UpdateTaskDtoValidator : AbstractValidator<UpdateTaskDto>
{
    public UpdateTaskDtoValidator(IRepository<AppUser> users)
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .Length(3, 200);

        RuleFor(x => x.Description)
            .MaximumLength(4000);

        RuleFor(x => x.Priority).IsInEnum();

        RuleFor(x => x.EstimatedHours)
            .GreaterThan(0)
            .LessThanOrEqualTo(9_999.99m)
            .PrecisionScale(6, 2, ignoreTrailingZeros: true)
            .When(x => x.EstimatedHours.HasValue);

        RuleFor(x => x.AssigneeId)
            .MustAsync(async (id, ct) => await users.ExistsAsync(u => u.Id == id!.Value, ct))
            .When(x => x.AssigneeId.HasValue)
            .WithMessage("User {PropertyValue} does not exist.");
    }
}

/// <summary>FluentValidation rules for <see cref="PatchTaskStatusDto"/>.</summary>
/// <remarks>
/// Only the enum's shape is checked here. Whether the transition is *legal* depends on
/// the task's current state, which this validator cannot see - that belongs to the
/// service, and produces a 409 rather than a 422.
/// </remarks>
public sealed class PatchTaskStatusDtoValidator : AbstractValidator<PatchTaskStatusDto>
{
    public PatchTaskStatusDtoValidator()
    {
        RuleFor(x => x.Status)
            .IsInEnum()
            .WithMessage("Status must be one of: Todo, InProgress, Blocked, Done, Cancelled.");
    }
}
