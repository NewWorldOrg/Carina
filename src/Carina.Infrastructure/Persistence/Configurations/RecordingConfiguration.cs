using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class RecordingConfiguration : IEntityTypeConfiguration<Recording>
{
    public const string InFlightIndexName = "ix_recording_in_flight";

    public const string SettledIndexName = "ix_recording_settled";

    public const string DroppedIndexName = "ix_recording_cc_dropped";

    public const string ReservationIndexName = "ix_recording_reservation";

    private static string Vocabulary<T>()
        where T : struct, Enum
        => "ARRAY[" + string.Join(", ", Enum.GetNames<T>().Select(name => $"'{name}'")) + "]::text[]";

    public void Configure(EntityTypeBuilder<Recording> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string faults = Vocabulary<RecordingFault>();
        string tuneFailures = Vocabulary<TuneFailureKind>();

        builder.ToTable("recording", table =>
        {
            table.HasCheckConstraint(
                "ck_recording_outcome",
                """
                (recording_outcome IS NULL OR recording_outcome IN ('Complete', 'Truncated', 'Failed'))
                AND (recording_outcome IS NULL OR stopped_at_actual IS NOT NULL)
                AND (recording_outcome IS NULL OR file_size_observed IS NOT NULL)
                """);
            table.HasCheckConstraint(
                "ck_recording_complete_was_asked_for",
                "recording_outcome IS DISTINCT FROM 'Complete' OR aborted_at IS NOT NULL");
            table.HasCheckConstraint(
                "ck_recording_empty_file_failed",
                "recording_outcome IS NULL OR file_size_observed <> 0 OR recording_outcome = 'Failed'");
            table.HasCheckConstraint(
                "ck_recording_outcome_detail",
                """
                recording_outcome IS NULL
                OR recording_outcome = 'Complete'
                OR recording_json_count(outcome_detail) > 0
                """);
            table.HasCheckConstraint(
                "ck_recording_history",
                $"recording_history_holds(interruptions, resume_count, {faults})");
            table.HasCheckConstraint(
                "ck_recording_reasons",
                $"recording_reasons_hold(outcome_detail, {faults}, {tuneFailures})");
            table.HasCheckConstraint(
                "ck_recording_positions",
                "recording_positions_hold(drop_positions, cc_dropped_packets, scrambled_packets)");
            table.HasCheckConstraint(
                "ck_recording_reanchors",
                $"recording_reanchors_hold(pcr_reanchors, {DropTimeline.PcrWrapsAt})");
            table.HasCheckConstraint(
                "ck_recording_measurement",
                """
                (cc_measured
                    OR (cc_dropped_packets IS NULL AND cc_total_packets IS NULL))
                AND (NOT cc_measured
                    OR (cc_dropped_packets IS NOT NULL
                        AND cc_total_packets IS NOT NULL
                        AND cc_dropped_packets <= cc_total_packets
                        AND measured_updated_at IS NOT NULL))
                """);
            table.HasCheckConstraint(
                "ck_recording_observation",
                "(file_size_observed IS NULL) = (observed_at IS NULL)");
            table.HasCheckConstraint("ck_recording_window", "expected_window_end > expected_window_start");
            table.HasCheckConstraint(
                "ck_recording_runs_forwards",
                """
                (stopped_at_actual IS NULL OR stopped_at_actual >= started_at_actual)
                AND (aborted_at IS NULL OR aborted_at >= started_at_actual)
                AND (observed_at IS NULL OR observed_at >= started_at_actual)
                AND (measured_updated_at IS NULL OR measured_updated_at >= started_at_actual)
                """);
            table.HasCheckConstraint(
                "ck_recording_tuner",
                """
                tuner_device_id IS NOT NULL
                OR (NOT cc_measured AND eovf_count = 0)
                """);
            table.HasCheckConstraint(
                "ck_recording_drop_positions",
                $"""
                (pcr_anchor IS NOT NULL
                    OR (recording_json_count(drop_positions) = 0 AND recording_json_count(pcr_reanchors) = 0))
                AND (pcr_anchor IS NULL OR cc_measured)
                AND (pcr_anchor IS NULL OR pcr_anchor BETWEEN 0 AND {DropTimeline.PcrWrapsAt - 1})
                """);
            table.HasCheckConstraint(
                "ck_recording_broadcast_group",
                """
                broadcast_group_role IN ('Standalone', 'MovementPrimary', 'MovementSuppressed', 'RelaySegment')
                AND (broadcast_group_role = 'Standalone' OR broadcast_group_key IS NOT NULL)
                """);
            table.HasCheckConstraint(
                "ck_recording_file_name",
                """
                btrim(file_name) = file_name
                AND length(file_name) > 0
                AND file_name <> '.'
                AND strpos(file_name, '/') = 0
                AND strpos(file_name, chr(92)) = 0
                AND strpos(file_name, '..') = 0
                """);
            table.HasCheckConstraint(
                "ck_recording_counts",
                """
                written_duration_ms >= 0
                AND resume_count >= 0
                AND eovf_count >= 0
                AND (file_size_observed IS NULL OR file_size_observed >= 0)
                AND (scrambled_packets IS NULL OR scrambled_packets >= 0)
                """);
        });

        builder.HasKey(recording => recording.Id);

        builder.Property(recording => recording.Id)
            .HasConversion(id => id.Value, value => new RecordingId(value))
            .HasColumnName("id");

        builder.Property(recording => recording.ReservationId)
            .HasConversion(id => id!.Value, value => new ReservationId(value))
            .HasColumnName("reservation_id");

        builder.Property(recording => recording.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id");

        builder.Property(recording => recording.ServiceId)
            .HasConversion(id => id.Value, value => new ServiceId(value))
            .HasColumnName("service_id");

        builder.Property(recording => recording.EventId)
            .HasConversion(id => id.Value, value => new EventId(value))
            .HasColumnName("event_id");

        builder.Property(recording => recording.ProgrammeStartsAt)
            .HasColumnName("programme_start_at")
            .IsRequired();

        builder.Property(recording => recording.OutputRoot)
            .HasConversion(root => root.Value, value => new OutputRoot(value))
            .HasMaxLength(Carina.Domain.Recordings.OutputRoot.MaxLength)
            .IsRequired();

        builder.Property(recording => recording.FileName)
            .HasConversion(name => name.Value, value => new RecordingFileName(value))
            .HasMaxLength(RecordingFileName.MaxLength)
            .IsRequired();

        builder.Property(recording => recording.FileSizeObserved);
        builder.Property(recording => recording.ObservedAt);
        builder.Property(recording => recording.StartedAtActual).IsRequired();
        builder.Property(recording => recording.StoppedAtActual);
        builder.Property(recording => recording.AbortedAt);
        builder.Property(recording => recording.WrittenDurationMs).IsRequired();
        builder.Property(recording => recording.ResumeCount).IsRequired();

        builder.Property(recording => recording.Interruptions)
            .HasConversion(
                interruptions => JsonSerializer.Serialize(interruptions, ProgrammeJson.Options),
                stored => Read<Interruption>(stored),
                Compared<Interruption>())
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(recording => recording.ExpectedWindowStart).IsRequired();
        builder.Property(recording => recording.ExpectedWindowEnd).IsRequired();

        builder.Property(recording => recording.Outcome)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasColumnName("recording_outcome");

        builder.Property(recording => recording.OutcomeDetail)
            .HasConversion(
                detail => JsonSerializer.Serialize(detail, ProgrammeJson.Options),
                stored => Read<OutcomeDetail>(stored),
                Compared<OutcomeDetail>())
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(recording => recording.CcMeasured).HasColumnName("cc_measured").IsRequired();
        builder.Property(recording => recording.CcDroppedPackets).HasColumnName("cc_dropped_packets");
        builder.Property(recording => recording.CcTotalPackets).HasColumnName("cc_total_packets");

        builder.ComplexProperty(recording => recording.Positions, positions =>
        {
            positions.IsRequired();

            positions.Ignore(timeline => timeline.Located);
            positions.Ignore(timeline => timeline.Continuity);
            positions.Ignore(timeline => timeline.Scrambled);

            positions.Property(timeline => timeline.AnchorPcr).HasColumnName("pcr_anchor");

            positions.Property(timeline => timeline.Buckets)
                .HasConversion(
                    buckets => JsonSerializer.Serialize(buckets, ProgrammeJson.Options),
                    stored => Read<DropBucket>(stored),
                    Compared<DropBucket>())
                .HasColumnName("drop_positions")
                .HasColumnType("jsonb")
                .IsRequired();

            positions.Property(timeline => timeline.Reanchors)
                .HasConversion(
                    reanchors => JsonSerializer.Serialize(reanchors, ProgrammeJson.Options),
                    stored => Read<PcrReanchor>(stored),
                    Compared<PcrReanchor>())
                .HasColumnName("pcr_reanchors")
                .HasColumnType("jsonb")
                .IsRequired();
        });

        builder.Property(recording => recording.ScrambledPackets);
        builder.Property(recording => recording.EovfCount).IsRequired();
        builder.Property(recording => recording.MeasuredUpdatedAt);

        builder.Property(recording => recording.TunerDeviceId)
            .HasConversion(id => id!.Value, value => new TunerDeviceId(value))
            .HasMaxLength(Carina.Domain.Recordings.TunerDeviceId.MaxLength);

        builder.Property(recording => recording.SnapshotName)
            .HasMaxLength(Reservation.NameMaxLength)
            .IsRequired();

        builder.Property(recording => recording.SnapshotSummary)
            .HasMaxLength(Reservation.SummaryMaxLength)
            .IsRequired();

        builder.Property(recording => recording.SnapshotExtended)
            .HasMaxLength(Reservation.ExtendedMaxLength)
            .IsRequired();

        builder.Property(recording => recording.SnapshotGenres)
            .HasConversion(
                genres => JsonSerializer.Serialize(genres, ProgrammeJson.Options),
                stored => Read<ProgrammeGenre>(stored),
                Compared<ProgrammeGenre>())
            .HasColumnName("snapshot_genres")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(recording => recording.CapturedAt).IsRequired();

        builder.Property(recording => recording.BroadcastGroupKey)
            .HasConversion(key => key!.Value, value => new BroadcastGroupKey(value))
            .HasMaxLength(Carina.Domain.Reservations.BroadcastGroupKey.MaxLength);

        builder.Property(recording => recording.BroadcastGroupRole)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Ignore(recording => recording.Counters);
        builder.Ignore(recording => recording.Programme);
        builder.Ignore(recording => recording.IsInFlight);
        builder.Ignore(recording => recording.Written);

        builder.HasIndex(recording => recording.StartedAtActual)
            .HasFilter("recording_outcome IS NULL")
            .HasDatabaseName(InFlightIndexName);

        builder.HasIndex(recording => new { recording.Outcome, recording.StoppedAtActual })
            .HasDatabaseName(SettledIndexName);

        builder.HasIndex(recording => recording.ReservationId)
            .HasFilter("reservation_id IS NOT NULL")
            .HasDatabaseName(ReservationIndexName);

        builder.HasIndex(recording => recording.CcDroppedPackets)
            .HasFilter("cc_measured AND cc_dropped_packets > 0")
            .HasDatabaseName(DroppedIndexName);
    }

    private static IReadOnlyList<T> Read<T>(string stored)
        => JsonSerializer.Deserialize<List<T>>(stored, ProgrammeJson.Options) ?? [];

    private static ValueComparer<IReadOnlyList<T>> Compared<T>()
        => new(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            list => list.Aggregate(0, (carried, item) => HashCode.Combine(carried, item)),
            list => list.ToList());
}
