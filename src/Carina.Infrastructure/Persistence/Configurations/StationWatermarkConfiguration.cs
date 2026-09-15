using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class StationWatermarkConfiguration : IEntityTypeConfiguration<StationWatermark>
{
    public const string LearnedAtIndexName = "ix_encode_watermark_learned_at";

    public void Configure(EntityTypeBuilder<StationWatermark> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("encode_watermark", table =>
        {
            table.HasCheckConstraint(
                "ck_encode_watermark_service",
                $"network_id BETWEEN {NetworkId.MinValue} AND {NetworkId.MaxValue} AND service_id BETWEEN {ServiceId.MinValue} AND {ServiceId.MaxValue}");
            table.HasCheckConstraint(
                "ck_encode_watermark_pattern",
                $"octet_length(pattern) = {WatermarkMask.PackedBytes}");
        });

        builder.HasKey(watermark => new { watermark.NetworkId, watermark.ServiceId, watermark.LearnedFrom });

        builder.Property(watermark => watermark.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id");

        builder.Property(watermark => watermark.ServiceId)
            .HasConversion(id => id.Value, value => new ServiceId(value))
            .HasColumnName("service_id");

        builder.Property(watermark => watermark.LearnedFrom)
            .HasConversion(id => id.Value, value => new RecordingId(value))
            .HasColumnName("learned_from");

        builder.Property(watermark => watermark.LearnedAt).IsRequired();
        builder.Property(watermark => watermark.Pattern).IsRequired();

        builder.Ignore(watermark => watermark.Mask);

        builder.HasIndex(watermark => new { watermark.NetworkId, watermark.ServiceId, watermark.LearnedAt })
            .HasDatabaseName(LearnedAtIndexName);
    }
}
