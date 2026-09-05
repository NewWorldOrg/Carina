using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class QualitySignalRollupConfiguration : IEntityTypeConfiguration<QualitySignalRollup>
{
    public const string TableName = "quality_signal_rollup";

    public const string RetentionIndexName = "ix_quality_signal_rollup_window_start";

    public void Configure(EntityTypeBuilder<QualitySignalRollup> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_quality_signal_rollup_granularity",
                $"granularity IN ({QualityVocabulary.Of<QualityWindow>()})");
            table.HasCheckConstraint(
                "ck_quality_signal_rollup_channel",
                $"""
                {QualityVocabulary.ABroadcastIdentifier("network_id")}
                AND {QualityVocabulary.ABroadcastIdentifier("service_id")}
                """);
            table.HasCheckConstraint(
                "ck_quality_signal_rollup_counts",
                """
                samples >= 0
                AND locked BETWEEN 0 AND samples
                AND unmeasured >= 0
                AND unreachable >= 0
                """);
            table.HasCheckConstraint(
                "ck_quality_signal_rollup_carrier_to_noise",
                """
                ((cnr_average IS NULL) = (cnr_lowest IS NULL))
                AND ((cnr_average IS NULL) = (cnr_highest IS NULL))
                AND (cnr_average IS NULL OR cnr_average BETWEEN cnr_lowest AND cnr_highest)
                """);
            table.HasCheckConstraint(
                "ck_quality_signal_rollup_bit_errors",
                "jsonb_typeof(bit_errors) = 'array'");
        });

        builder.HasKey(rollup => new
        {
            rollup.Granularity,
            rollup.WindowStart,
            rollup.Tuner,
            rollup.Network,
            rollup.Service,
        });

        builder.Property(rollup => rollup.Granularity)
            .HasConversion<string>()
            .HasMaxLength(QualityVocabulary.NameLength)
            .IsRequired();

        builder.Property(rollup => rollup.WindowStart).IsRequired();

        builder.Property(rollup => rollup.Tuner)
            .HasConversion(id => id.Value, stored => new TunerDeviceId(stored))
            .HasColumnName("tuner_device_id")
            .HasMaxLength(TunerDeviceId.MaxLength)
            .IsRequired();

        builder.Property(rollup => rollup.Network)
            .HasConversion(id => id.Value, stored => new NetworkId(stored))
            .HasColumnName("network_id")
            .IsRequired();

        builder.Property(rollup => rollup.Service)
            .HasConversion(id => id.Value, stored => new ServiceId(stored))
            .HasColumnName("service_id")
            .IsRequired();

        builder.Property(rollup => rollup.Samples).IsRequired();
        builder.Property(rollup => rollup.Locked).IsRequired();
        builder.Property(rollup => rollup.Unmeasured).IsRequired();
        builder.Property(rollup => rollup.Unreachable).IsRequired();
        builder.Property(rollup => rollup.CarrierToNoiseAverage).HasColumnName("cnr_average");
        builder.Property(rollup => rollup.CarrierToNoiseLowest).HasColumnName("cnr_lowest");
        builder.Property(rollup => rollup.CarrierToNoiseHighest).HasColumnName("cnr_highest");

        builder.Property(rollup => rollup.BitErrors)
            .HasConversion(
                rates => JsonSerializer.Serialize(rates, ProgrammeJson.Options),
                stored => Read(stored),
                Compared())
            .HasColumnName("bit_errors")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Ignore(rollup => rollup.LockRate);

        builder.HasIndex(rollup => new { rollup.Granularity, rollup.WindowStart }).HasDatabaseName(RetentionIndexName);
    }

    private static IReadOnlyList<LayerErrorRate> Read(string stored)
        => JsonSerializer.Deserialize<List<LayerErrorRate>>(stored, ProgrammeJson.Options) ?? [];

    private static ValueComparer<IReadOnlyList<LayerErrorRate>> Compared()
        => new(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            list => list.Aggregate(0, (carried, item) => HashCode.Combine(carried, item)),
            list => list.ToList());
}
