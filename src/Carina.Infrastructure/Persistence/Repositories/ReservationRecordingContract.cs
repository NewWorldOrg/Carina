using System.Data.Common;
using System.Text.Json;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class ReservationRecordingContract(CarinaDbContext context) : IReservationRecordingContract
{
    public const string View = "reservation_recording_tick";

    private const string DueAt = $"""
        SELECT id, network_id, service_id, event_id, programme_start_at, snapshot_name, priority,
               broadcast_group_key, broadcast_group_role, effective_start_at, effective_end_at,
               end_at_confirmed, started_at, snapshot_summary, snapshot_extended, snapshot_genres,
               captured_at, snapshot_audio, snapshot_sounds, margin_after, encode_when_recorded
        FROM {View}
        WHERE in_flight OR (effective_start_at <= $1 AND $1 < effective_end_at)
        ORDER BY effective_start_at, id
        """;

    public async Task<IReadOnlyList<RecordingTick>> DueAtAsync(DateTime at, CancellationToken cancellationToken)
    {
        DateTime moment = InUtc(at);

        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            DbConnection connection = context.Database.GetDbConnection();
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = DueAt;
            command.Parameters.Add(Moment(command, moment));

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

        DateTime moment = InUtc(at);

        List<long> claimed = await context.Database
            .SqlQuery<long>(
                $"UPDATE reservation SET started_at = {moment} WHERE id = {id.Value} AND started_at IS NULL AND state = 'Scheduled' RETURNING xmin::text::bigint AS \"Value\"")
            .ToListAsync(cancellationToken);

        return Moved(id, claimed);
    }

    public async Task<bool> ReleaseAsync(ReservationId id, DateTime claimedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        DateTime moment = InUtc(claimedAt);

        List<long> released = await context.Database
            .SqlQuery<long>(
                $"UPDATE reservation SET started_at = NULL WHERE id = {id.Value} AND started_at = {moment} AND recording_outcome IS NULL RETURNING xmin::text::bigint AS \"Value\"")
            .ToListAsync(cancellationToken);

        return Moved(id, released);
    }

    /// <summary>
    /// Carries the row version a claim statement left behind onto the copy of that reservation this
    /// context tracks, so a later write from the same context is not refused for a change it made itself.
    /// </summary>
    private bool Moved(ReservationId id, List<long> written)
    {
        if (written is not [long version])
        {
            return false;
        }

        foreach (EntityEntry<Reservation> tracked in context.ChangeTracker.Entries<Reservation>())
        {
            if (!tracked.Entity.Id.Equals(id))
            {
                continue;
            }

            PropertyEntry token = tracked.Property(ReservationConfiguration.ConcurrencyToken);

            token.OriginalValue = (uint)version;
            token.CurrentValue = (uint)version;
        }

        return true;
    }

    private static DateTime InUtc(DateTime at)
        => at.Kind is DateTimeKind.Utc
            ? at
            : throw new ArgumentException($"A tick is a UTC instant, but this one has Kind={at.Kind}.", nameof(at));

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
            new ProgrammeSnapshot(
                reader.GetString(5),
                reader.GetString(13),
                reader.GetString(14),
                JsonSerializer.Deserialize<List<ProgrammeGenre>>(reader.GetString(15), ProgrammeJson.Options) ?? [],
                reader.GetDateTime(16),
                Enum.Parse<AudioMode>(reader.GetString(17)),
                reader.GetInt32(18)),
            new Priority(reader.GetInt32(6)),
            reader.IsDBNull(7) ? null : new BroadcastGroupKey(reader.GetString(7)),
            Enum.Parse<BroadcastGroupRole>(reader.GetString(8)),
            reader.GetDateTime(9),
            reader.GetDateTime(10),
            reader.GetBoolean(11),
            TimeSpan.FromSeconds(reader.GetInt32(19)),
            reader.IsDBNull(12) ? null : reader.GetDateTime(12),
            reader.GetBoolean(20));
}
