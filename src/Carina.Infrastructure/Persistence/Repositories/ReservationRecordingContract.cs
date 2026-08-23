using System.Data.Common;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class ReservationRecordingContract(CarinaDbContext context) : IReservationRecordingContract
{
    public const string View = "reservation_recording_tick";

    private const string DueAt = $"""
        SELECT id, network_id, service_id, event_id, programme_start_at, snapshot_name, priority,
               broadcast_group_key, broadcast_group_role, effective_start_at, effective_end_at,
               end_at_confirmed, started_at
        FROM {View}
        WHERE in_flight OR (effective_start_at <= $1 AND $1 < effective_end_at)
        ORDER BY effective_start_at, id
        """;

    public async Task<IReadOnlyList<RecordingTick>> DueAtAsync(DateTime at, CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            DbConnection connection = context.Database.GetDbConnection();
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = DueAt;
            command.Parameters.Add(Moment(command, at));

            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            List<RecordingTick> due = [];

            while (await reader.ReadAsync(cancellationToken))
            {
                due.Add(Read(reader));
            }

            return due;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    public async Task<bool> ClaimAsync(ReservationId id, DateTime at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        int claimed = await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE reservation SET started_at = {at} WHERE id = {id.Value} AND started_at IS NULL AND state = 'Scheduled'",
            cancellationToken);

        return claimed is 1;
    }

    private static DbParameter Moment(DbCommand command, DateTime at)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.Value = at;

        return parameter;
    }

    private static RecordingTick Read(DbDataReader reader)
        => new(
            new ReservationId(reader.GetGuid(0)),
            new NetworkId(reader.GetInt32(1)),
            new ServiceId(reader.GetInt32(2)),
            new EventId(reader.GetInt32(3)),
            reader.GetDateTime(4),
            reader.GetString(5),
            new Priority(reader.GetInt32(6)),
            reader.IsDBNull(7) ? null : new BroadcastGroupKey(reader.GetString(7)),
            Enum.Parse<BroadcastGroupRole>(reader.GetString(8)),
            reader.GetDateTime(9),
            reader.GetDateTime(10),
            reader.GetBoolean(11),
            reader.IsDBNull(12) ? null : reader.GetDateTime(12));
}
