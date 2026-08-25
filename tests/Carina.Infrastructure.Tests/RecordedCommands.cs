using System.Data.Common;
using System.Text.Json;

using Microsoft.EntityFrameworkCore.Diagnostics;

using Npgsql;

namespace Carina.Infrastructure.Tests;

public sealed record RecordedCommand(string Text, IReadOnlyList<KeyValuePair<string, object?>> Parameters);

public sealed record QueryPlan(string Json, IReadOnlyList<string> NodeTypes, long SharedBlocks);

public sealed class RecordedCommands : DbCommandInterceptor
{
    private readonly List<RecordedCommand> seen = [];

    public IReadOnlyList<RecordedCommand> Seen => seen;

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

    public static async Task<QueryPlan> PlanForAsync(
        string connectionString,
        RecordedCommand recorded,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recorded);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var explaining = new NpgsqlCommand(
            "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + recorded.Text,
            connection);

        foreach (KeyValuePair<string, object?> carried in recorded.Parameters)
        {
            explaining.Parameters.Add(new NpgsqlParameter(carried.Key, carried.Value ?? DBNull.Value));
        }

        string json = (await explaining.ExecuteScalarAsync(cancellationToken))?.ToString()
            ?? throw new InvalidOperationException("EXPLAIN answered nothing.");

        using JsonDocument read = JsonDocument.Parse(json);
        JsonElement root = read.RootElement[0].GetProperty("Plan");
        List<string> nodes = [];
        Collect(root, nodes);

        return new QueryPlan(
            json,
            nodes,
            root.GetProperty("Shared Hit Blocks").GetInt64() + root.GetProperty("Shared Read Blocks").GetInt64());
    }

    private static void Collect(JsonElement node, List<string> nodes)
    {
        nodes.Add(node.GetProperty("Node Type").GetString() ?? string.Empty);

        if (node.TryGetProperty("Plans", out JsonElement children))
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                Collect(child, nodes);
            }
        }
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
