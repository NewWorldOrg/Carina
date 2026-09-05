using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class QualitySignalSampleConfiguration : IEntityTypeConfiguration<QualitySignalSample>
{
    public const string TableName = "quality_signal_sample";

    public const string RetentionIndexName = "ix_quality_signal_sample_taken_at";

    public const string TunerIndexName = "ix_quality_signal_sample_tuner_taken_at";

    public void Configure(EntityTypeBuilder<QualitySignalSample> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_quality_signal_sample_purpose",
                $"purpose IN ({QualityVocabulary.Of<SessionPurpose>()})");
            table.HasCheckConstraint(
                "ck_quality_signal_sample_channel",
                $"""
                {QualityVocabulary.ABroadcastIdentifier("network_id")}
                AND {QualityVocabulary.ABroadcastIdentifier("service_id")}
                """);
            table.HasCheckConstraint(
                "ck_quality_signal_sample_lock_gate",
                $"""
                locked
                OR (cnr_milli_decibels IS NULL AND {QualityVocabulary.AnEmptyList("bit_errors")})
                """);
            table.HasCheckConstraint(
                "ck_quality_signal_sample_read_at",
                $"""
                ((cnr_milli_decibels IS NULL) = (cnr_read_at IS NULL))
                AND (({QualityVocabulary.AnEmptyList("bit_errors")}) = (bit_errors_read_at IS NULL))
                AND jsonb_typeof(bit_errors) = 'array'
                AND jsonb_typeof(metrics_not_read) = 'array'
                """);
        });

        builder.HasKey(sample => new { sample.DriverInstanceId, sample.Session, sample.TakenAt });

        builder.Property(sample => sample.DriverInstanceId)
            .HasMaxLength(QualitySignalSample.DriverInstanceIdMaxLength)
            .IsRequired();

        builder.Property(sample => sample.Session)
            .HasConversion(session => session.Value!, stored => SessionId.Parse(stored))
            .HasColumnName("session_id")
            .HasMaxLength(SessionId.MaxLength)
            .IsRequired();

        builder.Property(sample => sample.TakenAt).IsRequired();

        builder.Property(sample => sample.Purpose)
            .HasConversion<string>()
            .HasMaxLength(QualityVocabulary.NameLength)
            .IsRequired();

        builder.Property(sample => sample.Tuner)
            .HasConversion(id => id.Value, stored => new TunerDeviceId(stored))
            .HasColumnName("tuner_device_id")
            .HasMaxLength(TunerDeviceId.MaxLength)
            .IsRequired();

        builder.Property(sample => sample.Network)
            .HasConversion(id => id.Value, stored => new NetworkId(stored))
            .HasColumnName("network_id")
            .IsRequired();

        builder.Property(sample => sample.Service)
            .HasConversion(id => id.Value, stored => new ServiceId(stored))
            .HasColumnName("service_id")
            .IsRequired();

        builder.ComplexProperty(sample => sample.Signal, signal =>
        {
            signal.Property(reading => reading.Locked).HasColumnName("locked");
            signal.Property(reading => reading.LockReadAt).HasColumnName("lock_read_at");
            signal.Property(reading => reading.CarrierToNoiseMilliDecibels).HasColumnName("cnr_milli_decibels");
            signal.Property(reading => reading.CarrierToNoiseReadAt).HasColumnName("cnr_read_at");
            signal.Property(reading => reading.BitErrorsReadAt).HasColumnName("bit_errors_read_at");

            signal.Property(reading => reading.BitErrors)
                .HasConversion(
                    counts => JsonSerializer.Serialize(counts, ProgrammeJson.Options),
                    stored => Read<LayerBitErrorCounts>(stored),
                    Compared<LayerBitErrorCounts>())
                .HasColumnName("bit_errors")
                .HasColumnType("jsonb")
                .IsRequired();

            signal.Property(reading => reading.MetricsNotRead)
                .HasConversion(
                    metrics => JsonSerializer.Serialize(metrics, ProgrammeJson.Options),
                    stored => Read<string>(stored),
                    Compared<string>())
                .HasColumnName("metrics_not_read")
                .HasColumnType("jsonb")
                .IsRequired();

            signal.Ignore(reading => reading.CarriesAnyValue);
        });

        builder.HasIndex(sample => sample.TakenAt).HasDatabaseName(RetentionIndexName);

        builder.HasIndex(sample => new { sample.Tuner, sample.TakenAt }).HasDatabaseName(TunerIndexName);
    }

    private static IReadOnlyList<T> Read<T>(string stored)
        => JsonSerializer.Deserialize<List<T>>(stored, ProgrammeJson.Options) ?? [];

    private static ValueComparer<IReadOnlyList<T>> Compared<T>()
        => new(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            list => list.Aggregate(0, (carried, item) => HashCode.Combine(carried, item)),
            list => list.ToList());
}
