using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace VRCX.Testing.Infrastructure;

/// <summary>
/// Counts the SQL commands the server sends, one per round trip (a SaveChanges batch of several
/// statements is one). Registered on the scenario's context factory, so it sees every EF command of
/// that server and nothing of other scenarios. Tests bound a flow's round trips with it:
/// <code>
/// var trips = await app.Sql.CountAsync(() => app.Jobs.FullSyncAsync());
/// await Assert.That(trips).IsLessThanOrEqualTo(40);
/// </code>
/// </summary>
public sealed class SqlCommandCounter : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _commands = new();

    /// <summary>The text of every command since the last <see cref="Reset"/>, oldest first.</summary>
    public IReadOnlyList<string> Commands => _commands.ToArray();

    public int Count => _commands.Count;

    public void Reset() => _commands.Clear();

    /// <summary>Runs <paramref name="work"/> and returns how many commands it sent.</summary>
    public async Task<int> CountAsync(Func<Task> work)
    {
        Reset();
        await work();
        return Count;
    }

    /// <summary>The commands so far, grouped by their text with a count each, for a failure message.</summary>
    public string Describe() => "the commands were:\n" + string.Join("\n", _commands
        .GroupBy(c => c.ReplaceLineEndings(" ") is var one && one.Length > 160 ? one[..160] + "..." : one)
        .Select(g => $"  {g.Count(),4} x {g.Key}"));

    private void Record(DbCommand command) => _commands.Enqueue(command.CommandText);

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => Record(command);

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Record(command);
        return Task.CompletedTask;
    }
}
