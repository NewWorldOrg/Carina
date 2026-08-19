using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class VisitTallyConfiguration : IEntityTypeConfiguration<VisitTally>
{
    public void Configure(EntityTypeBuilder<VisitTally> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "stream_visit_tally",
            table => table.HasCheckConstraint(
                "ck_stream_visit_tally_counts",
                "segments_declared >= segments_heard AND segments_heard >= 0"
                + " AND sections_declared >= 0 AND sections_heard >= 0 AND version_changes >= 0"));

        builder.HasKey(tally => new
        {
            tally.NetworkId,
            tally.TransportStreamId,
            tally.ServiceId,
            tally.TableId,
        });

        builder.Property(tally => tally.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id");

        builder.Property(tally => tally.TransportStreamId)
            .HasConversion(id => id.Value, value => new TransportStreamId(value))
            .HasColumnName("transport_stream_id");

        builder.Property(tally => tally.ServiceId)
            .HasConversion(id => id.Value, value => new ServiceId(value))
            .HasColumnName("service_id");

        builder.Property(tally => tally.TableId).IsRequired();
        builder.Property(tally => tally.LastTableId).IsRequired();
        builder.Property(tally => tally.SegmentsDeclared).IsRequired();
        builder.Property(tally => tally.SegmentsHeard).IsRequired();
        builder.Property(tally => tally.SectionsDeclared).IsRequired();
        builder.Property(tally => tally.SectionsHeard).IsRequired();
        builder.Property(tally => tally.VersionChanges).IsRequired();
    }
}
