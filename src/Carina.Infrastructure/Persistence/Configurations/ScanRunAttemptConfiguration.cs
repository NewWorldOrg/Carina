using Carina.Domain.Channels;
using Carina.Domain.Scans;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class ScanRunAttemptConfiguration : IEntityTypeConfiguration<ScanRunAttempt>
{
    public void Configure(EntityTypeBuilder<ScanRunAttempt> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("scan_run_attempt", table =>
        {
            table.HasCheckConstraint("ck_scan_run_attempt_tuning", PersistenceChecks.ReachableTuning);
            table.HasCheckConstraint(
                "ck_scan_run_attempt_outcome",
                "outcome IN ('Succeeded', 'NoLock', 'LockedWithoutData', 'IncompleteTables', 'UnexpectedStream')");
            table.HasCheckConstraint(
                "ck_scan_run_attempt_measurement_lock",
                PersistenceChecks.QualityOnlyWhenLocked("measured_at", "locked", "cnr_milli_decibels"));
            table.HasCheckConstraint("ck_scan_run_attempt_span", "finished_at >= started_at");
        });

        builder.HasKey(attempt => attempt.Id);

        builder.Property(attempt => attempt.Id)
            .HasConversion(id => id.Value, value => new ScanRunAttemptId(value))
            .HasColumnName("id");

        builder.Property(attempt => attempt.ScanRunId)
            .HasConversion(id => id.Value, value => new ScanRunId(value))
            .HasColumnName("scan_run_id");

        builder.ComplexProperty(attempt => attempt.Tuning, tuning =>
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

        builder.Property(attempt => attempt.Outcome)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.ComplexProperty(attempt => attempt.Measurement, measurement =>
        {
            measurement.IsRequired(false);

            measurement.Property(reading => reading.MeasuredAt).HasColumnName("measured_at");
            measurement.Property(reading => reading.Locked).HasColumnName("locked");
            measurement.Property(reading => reading.CnrMilliDecibels).HasColumnName("cnr_milli_decibels");
            measurement.Property(reading => reading.PostViterbiErrorBits).HasColumnName("post_viterbi_error_bits");
            measurement.Property(reading => reading.PostViterbiTotalBits).HasColumnName("post_viterbi_total_bits");
        });

        builder.Property(attempt => attempt.ObservedTransportStreamId)
            .HasConversion(id => id!.Value, value => new TransportStreamId(value))
            .HasColumnName("observed_transport_stream_id");

        builder.Property(attempt => attempt.Detail).HasMaxLength(ScanRunAttempt.DetailMaxLength);

        builder.Property(attempt => attempt.StartedAt).IsRequired();
        builder.Property(attempt => attempt.FinishedAt).IsRequired();

        builder.HasOne<ScanRun>()
            .WithMany()
            .HasForeignKey(attempt => attempt.ScanRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(attempt => new { attempt.ScanRunId, attempt.Outcome });
    }
}
