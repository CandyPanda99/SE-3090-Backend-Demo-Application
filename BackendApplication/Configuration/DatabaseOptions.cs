using System.ComponentModel.DataAnnotations;

namespace BackendApplication.Configuration;

/// <summary>
/// Strongly-typed settings for the database and its two connection pools, bound from
/// the <c>"Database"</c> section of appsettings.json.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Database";

    /// <summary>
    /// The PostgreSQL connection string without pooling keywords - those are appended
    /// from the properties below.
    /// </summary>
    [Required(ErrorMessage = "A database connection string is required.")]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Connections kept open even while idle ("warm" connections).</summary>
    [Range(0, 100)]
    public int MinPoolSize { get; set; } = 5;

    /// <summary>
    /// Hard ceiling on simultaneous physical connections from this instance.
    /// </summary>
    /// <remarks>
    /// PostgreSQL's own <c>max_connections</c> must exceed (replicas x this number),
    /// or a scale-up will exhaust the server rather than the pool.
    /// </remarks>
    [Range(1, 1000)]
    public int MaxPoolSize { get; set; } = 50;

    /// <summary>Seconds to wait for a free pooled connection before throwing.</summary>
    [Range(1, 300)]
    public int ConnectionTimeoutSeconds { get; set; } = 15;

    /// <summary>Seconds an idle connection may sit in the pool before being pruned.</summary>
    [Range(0, 3600)]
    public int ConnectionIdleLifetimeSeconds { get; set; } = 300;

    /// <summary>Recycles connections older than this many seconds, even if healthy.</summary>
    [Range(0, 86400)]
    public int ConnectionLifetimeSeconds { get; set; } = 1800;

    /// <summary>Seconds a single SQL statement may run before Npgsql cancels it.</summary>
    [Range(1, 600)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>How many DbContext instances EF Core keeps for reuse.</summary>
    [Range(1, 1024)]
    public int DbContextPoolSize { get; set; } = 128;

    /// <summary>Retries transient network/failover errors with exponential backoff.</summary>
    [Range(0, 10)]
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>Logs parameter values in SQL output. Development only.</summary>
    public bool EnableSensitiveDataLogging { get; set; }

    /// <summary>Logs generated SQL with timings at Information level. Development only.</summary>
    public bool EnableDetailedErrors { get; set; }

    /// <summary>Applies pending EF Core migrations automatically on startup.</summary>
    public bool ApplyMigrationsOnStartup { get; set; }

    /// <summary>Inserts demo users and tasks if the database is empty.</summary>
    public bool SeedDemoData { get; set; }

    /// <summary>
    /// Assembles the final Npgsql connection string, appending the pooling keywords.
    /// </summary>
    /// <remarks>
    /// Built here rather than written out in appsettings so the pooling numbers stay
    /// visible as named settings instead of hiding inside one long semicolon-separated
    /// string that nobody reads.
    /// </remarks>
    public string BuildConnectionString()
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Pooling = true,
            MinPoolSize = MinPoolSize,
            MaxPoolSize = MaxPoolSize,

            Timeout = ConnectionTimeoutSeconds,

            CommandTimeout = CommandTimeoutSeconds,

            ConnectionIdleLifetime = ConnectionIdleLifetimeSeconds,
            ConnectionLifetime = ConnectionLifetimeSeconds,

            ApplicationName = "BackendApplication.TasksApi",

            KeepAlive = 30
        };

        return builder.ConnectionString;
    }
}
