using Carina.Contracts;
using Carina.Domain.Channels;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class SatelliteTransportStreamConfiguration : IEntityTypeConfiguration<SatelliteTransportStream>
{
    public void Configure(EntityTypeBuilder<SatelliteTransportStream> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "satellite_transport_stream",
            table => table.HasCheckConstraint(
                "ck_satellite_transport_stream_slot",
                $"""
                bs_channel BETWEEN {BroadcastStandards.BsFirstChannel} AND {BroadcastStandards.BsLastChannel} AND bs_channel % 2 = 1 AND bs_channel NOT IN ({string.Join(", ", BroadcastStandards.BsChannelsWithoutDemodulation)})
                AND relative_stream_number BETWEEN {SatelliteTransportStream.FirstRelativeStreamNumber} AND {SatelliteTransportStream.LastRelativeStreamNumber}
                """));

        builder.HasKey(stream => new { stream.BsChannel, stream.RelativeStreamNumber });

        builder.Property(stream => stream.TransportStreamId)
            .HasConversion(id => id.Value, value => new TransportStreamId(value))
            .HasColumnName("transport_stream_id")
            .IsRequired();

        builder.HasData(SatelliteTransportStreamSeed.Rows);
    }
}
