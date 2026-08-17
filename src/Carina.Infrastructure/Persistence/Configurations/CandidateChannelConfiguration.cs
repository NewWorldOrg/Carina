using Carina.Domain.Channels;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class CandidateChannelConfiguration : IEntityTypeConfiguration<CandidateChannel>
{
    public const string SelectedIndexName = "ux_candidate_channel_selected";

    public void Configure(EntityTypeBuilder<CandidateChannel> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("candidate_channel", table =>
        {
            table.HasCheckConstraint("ck_candidate_channel_tuning", PersistenceChecks.ReachableTuning);
            table.HasCheckConstraint(
                "ck_candidate_channel_selection",
                "is_selected = (selection_source IS NOT NULL) AND is_selected = (selected_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_candidate_channel_rotation",
                """
                consecutive_failures >= 0
                AND (rotation_state <> 'NeedsAttention'
                     OR (needs_attention_since IS NOT NULL AND next_attempt_at IS NULL))
                AND (rotation_state <> 'BackingOff' OR next_attempt_at IS NOT NULL)
                AND (rotation_state <> 'Active'
                     OR (next_attempt_at IS NULL AND needs_attention_since IS NULL))
                """);
            table.HasCheckConstraint(
                "ck_candidate_channel_measurement_lock",
                PersistenceChecks.QualityOnlyWhenLocked("measured_at", "locked", "cnr_milli_decibels"));
            table.HasCheckConstraint(
                "ck_candidate_channel_selection_measurement_lock",
                PersistenceChecks.QualityOnlyWhenLocked(
                    "selected_measured_at", "selected_locked", "selected_cnr_milli_decibels"));
        });

        builder.HasKey(candidate => candidate.Id);

        builder.Property(candidate => candidate.Id)
            .HasConversion(id => id.Value, value => new CandidateChannelId(value))
            .HasColumnName("id");

        builder.Property(candidate => candidate.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id");

        builder.Property(candidate => candidate.ServiceId)
            .HasConversion(id => id.Value, value => new ServiceId(value))
            .HasColumnName("service_id");

        builder.ComplexProperty(candidate => candidate.Tuning, tuning =>
        {
            tuning.IsRequired();

            tuning.Property(parameters => parameters.System)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasColumnName("tune_system")
                .IsRequired();

            tuning.Property(parameters => parameters.PhysicalChannel)
                .HasColumnName("physical_channel")
                .IsRequired();

            tuning.Property(parameters => parameters.TransportStreamId)
                .HasConversion(id => id!.Value, value => new TransportStreamId(value))
                .HasColumnName("transport_stream_id");
        });

        builder.Property(candidate => candidate.IsSelected).IsRequired();

        builder.Property(candidate => candidate.SelectionSource)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.ComplexProperty(candidate => candidate.SelectionMeasurement, measurement =>
        {
            measurement.IsRequired(false);

            measurement.Property(reading => reading.MeasuredAt).HasColumnName("selected_measured_at");
            measurement.Property(reading => reading.Locked).HasColumnName("selected_locked");
            measurement.Property(reading => reading.CnrMilliDecibels).HasColumnName("selected_cnr_milli_decibels");
            measurement.Property(reading => reading.PostViterbiErrorBits)
                .HasColumnName("selected_post_viterbi_error_bits");
            measurement.Property(reading => reading.PostViterbiTotalBits)
                .HasColumnName("selected_post_viterbi_total_bits");
        });

        builder.ComplexProperty(candidate => candidate.LastMeasurement, measurement =>
        {
            measurement.IsRequired(false);

            measurement.Property(reading => reading.MeasuredAt).HasColumnName("measured_at");
            measurement.Property(reading => reading.Locked).HasColumnName("locked");
            measurement.Property(reading => reading.CnrMilliDecibels).HasColumnName("cnr_milli_decibels");
            measurement.Property(reading => reading.PostViterbiErrorBits).HasColumnName("post_viterbi_error_bits");
            measurement.Property(reading => reading.PostViterbiTotalBits).HasColumnName("post_viterbi_total_bits");
        });

        builder.Property(candidate => candidate.NeedsRevalidation).IsRequired();

        builder.Property(candidate => candidate.RotationState)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(candidate => candidate.ConsecutiveFailures).IsRequired();
        builder.Property(candidate => candidate.DiscoveredAt).IsRequired();
        builder.Property(candidate => candidate.LastSeenAt).IsRequired();

        builder.HasOne<BroadcastService>()
            .WithMany()
            .HasForeignKey(candidate => new { candidate.NetworkId, candidate.ServiceId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(candidate => new { candidate.NetworkId, candidate.ServiceId })
            .IsUnique()
            .HasFilter("is_selected")
            .HasDatabaseName(SelectedIndexName);

        builder.HasIndex(candidate => candidate.RotationState);
    }
}
