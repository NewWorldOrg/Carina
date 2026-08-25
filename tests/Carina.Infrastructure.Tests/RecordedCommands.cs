using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

using Npgsql;

namespace Carina.Infrastructure.Tests;

public sealed record RecordedCommand(string Text, IReadOnlyList<KeyValuePair<string, object?>> Parameters);

public sealed class RecordedCommands : DbCommandInterceptor
{
    private readonly List<RecordedCommand> seen = [];

    public IReadOnlyList<RecordedCommand> Seen => seen;

    public void Forget() => seen.Clear();

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Remember(command);

        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Remember(command);

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public static async Task<string> PlanForAsync(
        string connectionString,
        RecordedCommand recorded,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recorded);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var explaining = new NpgsqlCommand(
            "EXPLAIN (ANALYZE, BUFFERS) " + recorded.Text,
            connection);

        foreach (KeyValuePair<string, object?> carried in recorded.Parameters)
        {
            explaining.Parameters.Add(new NpgsqlParameter(carried.Key, carried.Value ?? DBNull.Value));
        }

        var lines = new List<string>();
        await using DbDataReader reader = await explaining.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join('\n', lines);
    }

    private void Remember(DbCommand command)
    {
        List<KeyValuePair<string, object?>> carried = [];

        foreach (DbParameter parameter in command.Parameters)
        {
            carried.Add(new KeyValuePair<string, object?>(parameter.ParameterName, parameter.Value));
        }

        seen.Add(new RecordedCommand(command.CommandText, carried));
    }
}
