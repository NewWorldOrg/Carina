using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class ReservationOutcomeConfiguration : IEntityTypeConfiguration<ReservationOutcome>
{
    public const string OccurrenceIndexName = "ix_reservation_outcome_occurred_at";

    public const string ReservationIndexName = "ux_reservation_outcome_reservation_kind";

    public void Configure(EntityTypeBuilder<ReservationOutcome> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("reservation_outcome", table =>
        {
            table.HasCheckConstraint(
                "ck_reservation_outcome_kind",
                "kind IN ('Competing', 'Missed', 'TuneFailure', 'RecordingFailure')");
            table.HasCheckConstraint(
                "ck_reservation_outcome_tune_failure",
                """
                (tune_failure IS NULL
                 OR tune_failure IN ('NoLock', 'NoData', 'IncompletePsi', 'StreamMismatch'))
                AND (kind <> 'TuneFailure' OR tune_failure IS NOT NULL)
                """);
            table.HasCheckConstraint(
                "ck_reservation_outcome_recording_outcome",
                """
                (recording_outcome IS NULL OR recording_outcome IN ('Complete', 'Truncated', 'Failed'))
                AND (kind <> 'RecordingFailure' OR recording_outcome IS NOT NULL)
                """);
            table.HasCheckConstraint(
                "ck_reservation_outcome_recorded_instead",
                "kind = 'Competing' OR jsonb_array_length(recorded_instead) = 0");
            table.HasCheckConstraint("ck_reservation_outcome_window", "effective_end_at > effective_start_at");
        });

        builder.HasKey(outcome => outcome.Id);

        builder.Property(outcome => outcome.Id)
            .HasConversion(id => id.Value, value => new ReservationOutcomeId(value))
            .HasColumnName("id");

        builder.Property(outcome => outcome.ReservationId)
            .HasConversion(id => id.Value, value => new ReservationId(value))
            .HasColumnName("reservation_id")
            .IsRequired();

        builder.Property(outcome => outcome.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id");

        builder.Property(outcome => outcome.ServiceId)
            .HasConversion(id => id.Value, value => new ServiceId(value))
            .HasColumnName("service_id");

        builder.Property(outcome => outcome.EventId)
            .HasConversion(id => id.Value, value => new EventId(value))
            .HasColumnName("event_id");

        builder.Property(outcome => outcome.ProgrammeStartsAt)
            .HasColumnName("programme_start_at")
            .IsRequired();

        builder.Property(outcome => outcome.SnapshotName)
            .HasMaxLength(Reservation.NameMaxLength)
            .IsRequired();

        builder.Property(outcome => outcome.EffectiveStartAt).IsRequired();
        builder.Property(outcome => outcome.EffectiveEndAt).IsRequired();

        builder.Property(outcome => outcome.Priority)
            .HasConversion(priority => priority.Value, value => new Priority(value))
            .IsRequired();

        builder.Property(outcome => outcome.RuleId)
            .HasConversion(id => id!.Value, value => new RuleId(value))
            .HasColumnName("rule_id");

        builder.Property(outcome => outcome.Kind)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(outcome => outcome.TuneFailure)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(outcome => outcome.RecordingOutcome)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(outcome => outcome.RecordedInstead)
            .HasConversion(
                instead => JsonSerializer.Serialize(instead, ProgrammeJson.Options),
                stored => Read(stored),
                Compared())
            .HasColumnName("recorded_instead")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(outcome => outcome.OccurredAt).IsRequired();

        builder.HasIndex(outcome => outcome.OccurredAt).HasDatabaseName(OccurrenceIndexName);
        builder.HasIndex(outcome => new { outcome.ReservationId, outcome.Kind })
            .HasDatabaseName(ReservationIndexName)
            .IsUnique();
    }

    private static IReadOnlyList<Guid> Read(string stored)
        => JsonSerializer.Deserialize<List<Guid>>(stored, ProgrammeJson.Options) ?? [];

    private static ValueComparer<IReadOnlyList<Guid>> Compared()
        => new(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            list => list.Aggregate(0, (carried, item) => HashCode.Combine(carried, item)),
            list => list.ToList());
}
