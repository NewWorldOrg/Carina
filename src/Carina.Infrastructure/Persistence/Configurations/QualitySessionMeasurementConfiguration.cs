using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class QualitySessionMeasurementConfiguration : IEntityTypeConfiguration<QualitySessionMeasurement>
{
    public const string TableName = "quality_session_measurement";

    public const string StartedIndexName = "ix_quality_session_measurement_started_at";

    public void Configure(EntityTypeBuilder<QualitySessionMeasurement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_quality_session_measurement_purpose",
                $"""
                purpose IN ({QualityVocabulary.Of<SessionPurpose>()})
                AND purpose <> '{nameof(SessionPurpose.Recording)}'
                """);
            table.HasCheckConstraint(
                "ck_quality_session_measurement_channel",
                $"""
                {QualityVocabulary.ABroadcastIdentifier("network_id")}
                AND {QualityVocabulary.ABroadcastIdentifier("service_id")}
                """);
            table.HasCheckConstraint(
                "ck_quality_session_measurement_counts",
                """
                (cc_measured = (cc_dropped_packets IS NOT NULL AND cc_total_packets IS NOT NULL))
                AND (cc_measured = (measured_updated_at IS NOT NULL))
                AND (cc_dropped_packets IS NULL OR cc_dropped_packets >= 0)
                AND (cc_total_packets IS NULL OR cc_total_packets >= 0)
                AND eovf_count >= 0
                """);
            table.HasCheckConstraint(
                "ck_quality_session_measurement_span",
                "ended_at IS NULL OR ended_at >= started_at");
        });

        builder.HasKey(measurement => new { measurement.DriverInstanceId, measurement.Session });

        builder.Property(measurement => measurement.DriverInstanceId)
            .HasMaxLength(QualitySessionMeasurement.DriverInstanceIdMaxLength)
            .IsRequired();

        builder.Property(measurement => measurement.Session)
            .HasConversion(session => session.Value!, stored => SessionId.Parse(stored))
            .HasColumnName("session_id")
            .HasMaxLength(SessionId.MaxLength)
            .IsRequired();

        builder.Property(measurement => measurement.Purpose)
            .HasConversion<string>()
            .HasMaxLength(QualityVocabulary.NameLength)
            .IsRequired();

        builder.Property(measurement => measurement.Tuner)
            .HasConversion(id => id.Value, stored => new TunerDeviceId(stored))
            .HasColumnName("tuner_device_id")
            .HasMaxLength(TunerDeviceId.MaxLength)
            .IsRequired();

        builder.Property(measurement => measurement.Network)
            .HasConversion(id => id.Value, stored => new NetworkId(stored))
            .HasColumnName("network_id")
            .IsRequired();

        builder.Property(measurement => measurement.Service)
            .HasConversion(id => id.Value, stored => new ServiceId(stored))
            .HasColumnName("service_id")
            .IsRequired();

        builder.Property(measurement => measurement.StartedAt).IsRequired();
        builder.Property(measurement => measurement.EndedAt);
        builder.Property(measurement => measurement.CcMeasured).HasColumnName("cc_measured").IsRequired();
        builder.Property(measurement => measurement.CcDroppedPackets).HasColumnName("cc_dropped_packets");
        builder.Property(measurement => measurement.CcTotalPackets).HasColumnName("cc_total_packets");
        builder.Property(measurement => measurement.EovfCount).IsRequired();
        builder.Property(measurement => measurement.MeasuredUpdatedAt);

        builder.Ignore(measurement => measurement.HasEnded);

        builder.HasIndex(measurement => measurement.StartedAt).HasDatabaseName(StartedIndexName);
    }
}
