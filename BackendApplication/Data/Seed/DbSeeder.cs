using BackendApplication.Domain.Entities;
using BackendApplication.Domain.Enums;

namespace BackendApplication.Data.Seed;

/// <summary>
/// Populates an empty database with demo users and a realistic board of tasks.
/// </summary>
/// <remarks>
/// Idempotent: it checks for existing rows first, so it is safe to call on every
/// startup. Seeded data exists so the API is explorable the moment it boots - an empty
/// catalogue makes every GET look broken.
/// </remarks>
public static class DbSeeder
{
    /// <summary>Seeds the database if it is empty. Safe to call on every startup.</summary>
    public static async Task SeedAsync(TasksDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (!await db.Users.AnyAsync(ct))
        {
            logger.LogInformation("Seeding demo users...");

            db.Users.AddRange(
                NewUser("admin@tasks.local", "Admin#12345", "Board Admin", UserRole.Admin),
                NewUser("lead@tasks.local", "Lead#12345", "Team Lead", UserRole.Lead),
                NewUser("member@tasks.local", "Member#12345", "Sample Member", UserRole.Member));

            await db.SaveChangesAsync(ct);
        }

        if (await db.Tasks.AnyAsync(ct))
        {
            logger.LogInformation("Database already seeded - skipping task seed.");
            return;
        }

        logger.LogInformation("Seeding demo tasks...");

        // Look the users back up so the tasks can reference real ids.
        var admin = await db.Users.FirstAsync(u => u.Email == "admin@tasks.local", ct);
        var lead = await db.Users.FirstAsync(u => u.Email == "lead@tasks.local", ct);
        var member = await db.Users.FirstAsync(u => u.Email == "member@tasks.local", ct);

        // Everything is relative to "now" so the board always contains genuinely overdue
        // and genuinely upcoming work, however long after this was written it is run.
        var now = DateTime.UtcNow;

        db.Tasks.AddRange(
            Todo("TSK-0001", "Write the project charter", "One page: goal, scope, who decides what.", TaskPriority.High, now.AddDays(3), 4m, lead.Id),
            Todo("TSK-0002", "Pick a component library", "Compare three options against our accessibility checklist.", TaskPriority.Medium, now.AddDays(10), 6m, null),
            Todo("TSK-0003", "Draft the onboarding README", "Someone new should get running in under ten minutes.", TaskPriority.Low, now.AddDays(21), 3m, member.Id),
            Todo("TSK-0004", "Budget the staging environment", "Monthly cost for the database and two app instances.", TaskPriority.Medium, null, null, admin.Id),
            Todo("TSK-0005", "Schedule the accessibility audit", "Book the external reviewer for the sprint after next.", TaskPriority.Low, now.AddDays(30), 1m, null),

            InProgress("TSK-0006", "Add pagination to the reports screen", "It loads every row today. Offset paging, page size 20.", TaskPriority.High, now.AddDays(2), 8m, member.Id),
            InProgress("TSK-0007", "Migrate the build to the new runner", "The old image is out of support next month.", TaskPriority.Critical, now.AddDays(-1), 12m, lead.Id),
            InProgress("TSK-0008", "Write integration tests for checkout", "Assert against the database, not a mocked repository.", TaskPriority.High, now.AddDays(5), 10m, member.Id),
            InProgress("TSK-0009", "Instrument the slow-query log", "Wire the threshold to configuration instead of a constant.", TaskPriority.Medium, now.AddDays(7), 5m, admin.Id),

            Blocked("TSK-0010", "Enable SSO for the admin portal", "Waiting on the identity provider tenant from IT.", TaskPriority.High, now.AddDays(-4), 16m, lead.Id),
            Blocked("TSK-0011", "Sign the data processing agreement", "Legal has had it for two weeks.", TaskPriority.Critical, now.AddDays(-9), 2m, admin.Id),

            Done("TSK-0012", "Set up the PostgreSQL container", "Compose service with a healthcheck and a named volume.", TaskPriority.High, now.AddDays(-14), 3m, lead.Id, now.AddDays(-12)),
            Done("TSK-0013", "Agree the branching strategy", "Trunk-based, short-lived branches, squash on merge.", TaskPriority.Medium, now.AddDays(-20), 2m, admin.Id, now.AddDays(-18)),
            Done("TSK-0014", "Choose the logging format", "Structured, single line, correlation id on every entry.", TaskPriority.Low, now.AddDays(-25), 1.5m, member.Id, now.AddDays(-24)),
            Done("TSK-0015", "Provision the shared dev database", "One database, one schema per developer.", TaskPriority.Medium, now.AddDays(-30), 4m, lead.Id, now.AddDays(-28)),

            Cancelled("TSK-0016", "Evaluate the NoSQL option", "Superseded - the data is relational and the joins are the point.", TaskPriority.Low, null, 8m, null),
            Cancelled("TSK-0017", "Build a custom chart library", "Not worth it. Using an existing one instead.", TaskPriority.Medium, now.AddDays(-6), 40m, member.Id));

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Seed complete: {Tasks} tasks, {Users} users.",
            await db.Tasks.CountAsync(ct),
            await db.Users.CountAsync(ct));
    }

    private static AppUser NewUser(string email, string password, string fullName, UserRole role) => new()
    {
        Email = email,

        // Work factor 10 here rather than the 12 used at registration: the seeder hashes
        // three passwords on every cold start, and 12 makes that noticeably slow for no
        // security benefit on throwaway demo accounts.
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 10),

        FullName = fullName,
        Role = role,
        IsActive = true
    };

    private static TaskItem Todo(
        string reference, string title, string description,
        TaskPriority priority, DateTime? dueAt, decimal? hours, int? assigneeId)
        => Build(reference, title, description, TaskItemStatus.Todo, priority, dueAt, hours, assigneeId, null);

    private static TaskItem InProgress(
        string reference, string title, string description,
        TaskPriority priority, DateTime? dueAt, decimal? hours, int assigneeId)
        => Build(reference, title, description, TaskItemStatus.InProgress, priority, dueAt, hours, assigneeId, null);

    private static TaskItem Blocked(
        string reference, string title, string description,
        TaskPriority priority, DateTime? dueAt, decimal? hours, int assigneeId)
        => Build(reference, title, description, TaskItemStatus.Blocked, priority, dueAt, hours, assigneeId, null);

    private static TaskItem Done(
        string reference, string title, string description,
        TaskPriority priority, DateTime? dueAt, decimal? hours, int assigneeId, DateTime completedAt)
        => Build(reference, title, description, TaskItemStatus.Done, priority, dueAt, hours, assigneeId, completedAt);

    private static TaskItem Cancelled(
        string reference, string title, string description,
        TaskPriority priority, DateTime? dueAt, decimal? hours, int? assigneeId)
        => Build(reference, title, description, TaskItemStatus.Cancelled, priority, dueAt, hours, assigneeId, null);

    /// <summary>
    /// One constructor for every seeded row.
    /// </summary>
    /// <remarks>
    /// <paramref name="completedAt"/> must be set for, and only for, Done tasks - the
    /// <c>CK_Tasks_CompletedAt_Matches_Status</c> check constraint rejects any other
    /// combination. The typed helpers above exist so that pairing cannot be got wrong.
    /// </remarks>
    private static TaskItem Build(
        string reference, string title, string description,
        TaskItemStatus status, TaskPriority priority,
        DateTime? dueAt, decimal? hours, int? assigneeId, DateTime? completedAt) => new()
    {
        Reference = reference,
        Title = title,
        Description = description,
        Status = status,
        Priority = priority,
        DueAtUtc = dueAt,
        EstimatedHours = hours,
        AssigneeId = assigneeId,
        CompletedAtUtc = completedAt
    };
}
