using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BackendApplication.Data.Interceptors;

/// <summary>
/// Logs a warning for any SQL statement that takes longer than a threshold.
/// </summary>
/// <remarks>
/// The database-side twin of PostgreSQL's <c>log_min_duration_statement</c>. Having it
/// here as well means the slow statement is logged with the application's correlation id
/// attached, so a slow request and the query that caused it can be lined up.
/// </remarks>
public sealed class SlowQueryLoggingInterceptor : DbCommandInterceptor
{
    /// <summary>Statements slower than this are logged at Warning.</summary>
    private const int ThresholdMilliseconds = 300;

    private readonly ILogger<SlowQueryLoggingInterceptor> _logger;

    public SlowQueryLoggingInterceptor(ILogger<SlowQueryLoggingInterceptor> logger) => _logger = logger;

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        Log(command, eventData);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Log(command, eventData);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        Log(command, eventData);
        return base.NonQueryExecuted(command, eventData, result);
    }

    private void Log(DbCommand command, CommandExecutedEventData eventData)
    {
        if (eventData.Duration.TotalMilliseconds < ThresholdMilliseconds)
        {
            return;
        }

        _logger.LogWarning(
            "Slow query took {ElapsedMs:0}ms (threshold {ThresholdMs}ms): {Sql}",
            eventData.Duration.TotalMilliseconds,
            ThresholdMilliseconds,
            Truncate(command.CommandText));
    }

    /// <summary>Keeps one runaway statement from filling the log file.</summary>
    private static string Truncate(string sql)
        => sql.Length <= 500 ? sql : string.Concat(sql.AsSpan(0, 500), "... [truncated]");
}
