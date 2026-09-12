using BackendApplication.Domain.Entities;
using BackendApplication.Domain.Enums;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BackendApplication.Data.Configurations;

/// <summary>
/// What <see cref="TaskItem"/> cannot say with attributes: check constraints, database
/// defaults, a partial index, value conversion and delete behaviour.
/// </summary>
public class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        // The table NAME comes from [Table("Tasks")] on the entity; this overload adds to that
        // table without renaming it. Validation protects the API, but CHECK constraints protect
        // the DATA - a rogue psql session cannot insert a negative estimate.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_Tasks_EstimatedHours_Positive",
                "\"EstimatedHours\" IS NULL OR \"EstimatedHours\" > 0");

            // Done is the only status allowed to carry a completion timestamp, and it must
            // carry one. Expressing it here means the pairing holds even for rows written by
            // a migration or an admin script that never touches the C# entity.
            t.HasCheckConstraint(
                "CK_Tasks_CompletedAt_Matches_Status",
                "(\"Status\" = 'Done' AND \"CompletedAtUtc\" IS NOT NULL) " +
                "OR (\"Status\" <> 'Done' AND \"CompletedAtUtc\" IS NULL)");
        });

        // Stored as "Todo"/"InProgress" rather than 0/1, plus a database DEFAULT. Neither the
        // conversion nor the default has an attribute form.
        builder.Property(t => t.Status)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired()
               .HasDefaultValue(TaskItemStatus.Todo);

        // Deliberately NO HasDefaultValue here, unlike Status above, and the reason is a
        // trap worth knowing about.
        //
        // EF decides whether to send a column on INSERT by comparing the property against
        // the CLR default for its type - for an enum, that is 0. Status gets away with a
        // database default because TaskItemStatus.Todo IS 0, so "unset" and "the default"
        // mean the same thing. TaskPriority.Low is also 0, but the sensible database
        // default is Medium - so with HasDefaultValue(Medium), a task explicitly created
        // as Low would look "unset" to EF, the column would be omitted, and PostgreSQL
        // would store Medium. The caller's choice would vanish with no error anywhere.
        //
        // The C# initialiser on the entity (= TaskPriority.Medium) gives new tasks their
        // default instead, which EF always sends because it is a real assigned value.
        builder.Property(t => t.Priority)
               .HasConversion<string>()
               .HasMaxLength(20)
               .IsRequired();

        // PARTIAL index - only indexes the rows we actually query. [Index] has no filter
        // option, so this one index stays here while the other three are attributes.
        builder.HasIndex(t => t.Title)
               .HasFilter("\"IsDeleted\" = false")
               .HasDatabaseName("IX_Tasks_Title_Active");

        // SetNull, not Cascade: deleting a user must not delete their tasks. The work
        // outlives the person who happened to own it, and the task simply becomes
        // unassigned. The convention would cascade here, silently destroying history.
        builder.HasOne(t => t.Assignee)
               .WithMany(u => u.AssignedTasks)
               .HasForeignKey(t => t.AssigneeId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.Property(t => t.IsDeleted).HasDefaultValue(false);
    }
}
