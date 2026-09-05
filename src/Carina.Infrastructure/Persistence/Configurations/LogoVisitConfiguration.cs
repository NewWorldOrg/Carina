using Carina.Domain.Channels;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class LogoVisitConfiguration : IEntityTypeConfiguration<LogoVisit>
{
    public void Configure(EntityTypeBuilder<LogoVisit> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("logo_visit", table =>
        {
            table.HasCheckConstraint(
                "ck_logo_visit_outcome",
                "outcome IN ('Collected', 'NothingArrived', 'NoLock', 'Interrupted')");
            table.HasCheckConstraint(
                "ck_logo_visit_collected",
                "(outcome <> 'Collected') OR (last_collected_at IS NOT NULL)");
        });

        builder.HasKey(visit => new { visit.NetworkId, visit.TransportStreamId });

        builder.Property(visit => visit.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id");

        builder.Property(visit => visit.TransportStreamId)
            .HasConversion(id => id.Value, value => new TransportStreamId(value))
            .HasColumnName("transport_stream_id");

        builder.Property(visit => visit.Outcome)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(visit => visit.LastAttemptedAt).IsRequired();
        builder.Property(visit => visit.LastCollectedAt);
    }
}
