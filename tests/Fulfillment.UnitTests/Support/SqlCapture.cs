using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fulfillment.UnitTests.Support;

/// <summary>Records every SQL command EF actually sends, so tests can inspect the real queries rather than a re-typed copy.</summary>
internal sealed class SqlCapture : DbCommandInterceptor
{
    private readonly List<CapturedCommand> _commands = [];

    public IReadOnlyList<CapturedCommand> Commands => _commands;

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    private void Record(DbCommand command) => _commands.Add(new CapturedCommand(
        command.CommandText,
        command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, p.Value)).ToList()));
}

internal sealed record CapturedCommand(string Sql, IReadOnlyList<(string Name, object? Value)> Parameters);
