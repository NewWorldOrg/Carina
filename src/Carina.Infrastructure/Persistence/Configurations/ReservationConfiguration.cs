using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public const string ProgrammeIndexName = "ux_reservation_programme";

    public const string WindowIndexName = "ix_reservation_window";

    public const string ClaimableIndexName = "ix_reservation_claimable";

    public const string BroadcastGroupIndexName = "ix_reservation_broadcast_group";

    public const string CompositeState = "composite_state";

    public const string ClaimColumn = "started_at";

    public const string OutcomeColumn = "recording_outcome";

    public static readonly IReadOnlyList<string> RecordingOwnedColumns = [ClaimColumn, OutcomeColumn];

    private const string CompositeStateSql = """
        CASE
            WHEN recording_outcome IS NOT NULL THEN recording_outcome
            WHEN started_at IS NOT NULL THEN 'Recording'
            ELSE state
        END
        """;

    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("reservation", table =>
        {
            table.HasCheckConstraint(
                "ck_reservation_state",
                "state IN ('Scheduled', 'Conflict', 'Cancelled', 'Missed')");
            table.HasCheckConstraint(
                "ck_reservation_broadcast_group",
                """
                broadcast_group_role IN ('Standalone', 'MovementPrimary', 'MovementSuppressed', 'RelaySegment')
                AND (broadcast_group_role = 'Standalone' OR broadcast_group_key IS NOT NULL)
                """);
            table.HasCheckConstraint(
                "ck_reservation_recording_outcome",
                """
                recording_outcome IS NULL
                OR (recording_outcome IN ('Complete', 'Truncated', 'Failed') AND started_at IS NOT NULL)
                """);
            table.HasCheckConstraint(
                "ck_reservation_margins",
                $"margin_before BETWEEN 0 AND {(int)Margin.Longest.TotalSeconds} "
                + $"AND margin_after BETWEEN 0 AND {(int)Margin.Longest.TotalSeconds}");
            table.HasCheckConstraint("ck_reservation_window", "end_at > start_at");
            table.HasCheckConstraint(
                "ck_reservation_priority",
                $"priority BETWEEN {Priority.MinValue} AND {Priority.MaxValue}");
            table.HasCheckConstraint(
                "ck_reservation_divergence",
                """
                epg_diverged = (jsonb_array_length(epg_diverged_detail) > 0)
                AND (acknowledged_at IS NULL OR epg_diverged OR epg_missing)
                """);
        });

        builder.HasKey(reservation => reservation.Id);

        builder.Property(reservation => reservation.Id)
            .HasConversion(id => id.Value, value => new ReservationId(value))
            .HasColumnName("id");

        builder.Property(reservation => reservation.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id");

        builder.Property(reservation => reservation.ServiceId)
            .HasConversion(id => id.Value, value => new ServiceId(value))
            .HasColumnName("service_id");

        builder.Property(reservation => reservation.EventId)
            .HasConversion(id => id.Value, value => new EventId(value))
            .HasColumnName("event_id");

        builder.Property(reservation => reservation.ProgrammeStartsAt)
            .HasColumnName("programme_start_at")
            .IsRequired();

        builder.Property(reservation => reservation.RuleId)
            .HasConversion(id => id!.Value, value => new RuleId(value))
            .HasColumnName("rule_id");

        builder.Property(reservation => reservation.Priority)
            .HasConversion(priority => priority.Value, value => new Priority(value))
            .HasColumnName("priority")
            .IsRequired();

        builder.Property(reservation => reservation.StartAt).IsRequired();
        builder.Property(reservation => reservation.EndAt).IsRequired();
        builder.Property(reservation => reservation.EndAtConfirmed).IsRequired();

        builder.Property(reservation => reservation.MarginBefore)
            .HasConversion(margin => margin.Seconds, value => Margin.OfSeconds(value))
            .HasColumnName("margin_before")
            .IsRequired();

        builder.Property(reservation => reservation.MarginAfter)
            .HasConversion(margin => margin.Seconds, value => Margin.OfSeconds(value))
            .HasColumnName("margin_after")
            .IsRequired();

        builder.Property(reservation => reservation.SnapshotName)
            .HasMaxLength(Reservation.NameMaxLength)
            .IsRequired();

        builder.Property(reservation => reservation.SnapshotSummary)
            .HasMaxLength(Reservation.SummaryMaxLength)
            .IsRequired();

        builder.Property(reservation => reservation.SnapshotExtended)
            .HasMaxLength(Reservation.ExtendedMaxLength)
            .IsRequired();

        builder.Property(reservation => reservation.SnapshotGenres)
            .HasConversion(
                genres => JsonSerializer.Serialize(genres, ProgrammeJson.Options),
                stored => Read<ProgrammeGenre>(stored),
                Compared<ProgrammeGenre>())
            .HasColumnName("snapshot_genres")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(reservation => reservation.CapturedAt).IsRequired();

        builder.Property(reservation => reservation.EpgDiverged).IsRequired();

        builder.Property(reservation => reservation.EpgDivergences)
            .HasConversion(
                divergences => JsonSerializer.Serialize(divergences, ProgrammeJson.Options),
                stored => Read<EpgDivergence>(stored),
                Compared<EpgDivergence>())
            .HasColumnName("epg_diverged_detail")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(reservation => reservation.EpgMissing).IsRequired();
        builder.Property(reservation => reservation.AcknowledgedAt);

        builder.Property(reservation => reservation.BroadcastGroupKey)
            .HasConversion(key => key!.Value, value => new BroadcastGroupKey(value))
            .HasMaxLength(Carina.Domain.Reservations.BroadcastGroupKey.MaxLength);

        builder.Property(reservation => reservation.BroadcastGroupRole)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(reservation => reservation.State)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(reservation => reservation.CreatedAt).IsRequired();

        RelinquishToRecording(builder.Property(reservation => reservation.StartedAt).HasColumnName("started_at"));
        RelinquishToRecording(
            builder.Property(reservation => reservation.RecordingOutcome)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasColumnName("recording_outcome"));

        builder.Ignore(reservation => reservation.Programme);
        builder.Ignore(reservation => reservation.EffectiveStartAt);
        builder.Ignore(reservation => reservation.EffectiveEndAt);
        builder.Ignore(reservation => reservation.IsPinned);
        builder.Ignore(reservation => reservation.IsRuleBorn);

        builder.Property<string>(CompositeState)
            .HasColumnName(CompositeState)
            .HasColumnType("text")
            .HasComputedColumnSql(CompositeStateSql, stored: true);

        builder.HasOne<Rule>()
            .WithMany()
            .HasForeignKey(reservation => reservation.RuleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(reservation => new
        {
            reservation.NetworkId,
            reservation.ServiceId,
            reservation.EventId,
            reservation.ProgrammeStartsAt,
        })
            .IsUnique()
            .HasDatabaseName(ProgrammeIndexName);

        builder.HasIndex(reservation => new { reservation.StartAt, reservation.EndAt })
            .HasFilter("state IN ('Scheduled', 'Conflict')")
            .HasDatabaseName(WindowIndexName);

        builder.HasIndex(reservation => reservation.StartAt)
            .HasFilter("started_at IS NULL AND state = 'Scheduled'")
            .HasDatabaseName(ClaimableIndexName);

        builder.HasIndex(reservation => reservation.BroadcastGroupKey)
            .HasDatabaseName(BroadcastGroupIndexName);
    }

    private static void RelinquishToRecording(PropertyBuilder builder)
    {
        builder.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }

    private static IReadOnlyList<T> Read<T>(string stored)
        => JsonSerializer.Deserialize<List<T>>(stored, ProgrammeJson.Options) ?? [];

    private static ValueComparer<IReadOnlyList<T>> Compared<T>()
        => new(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            list => list.Aggregate(0, (carried, item) => HashCode.Combine(carried, item)),
            list => list.ToList());
}
