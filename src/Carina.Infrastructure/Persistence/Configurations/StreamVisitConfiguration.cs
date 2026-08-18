using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class StreamVisitConfiguration : IEntityTypeConfiguration<StreamVisit>
{
    public void Configure(EntityTypeBuilder<StreamVisit> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "stream_visit",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_stream_visit_outcome",
                    "outcome IN ('Complete', 'BasicOnly', 'Incomplete', 'Interrupted', 'NoLock', 'NoBytes')");
                table.HasCheckConstraint(
                    "ck_stream_visit_counts",
                    "consecutive_incomplete >= 0 AND last_duration_milliseconds >= 0");
                table.HasCheckConstraint(
                    "ck_stream_visit_completion",
                    "last_completed_at IS NULL OR last_completed_at <= last_attempted_at");
            });

        builder.HasKey(visit => new { visit.NetworkId, visit.TransportStreamId });

        builder.Property(visit => visit.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id");

        builder.Property(visit => visit.TransportStreamId)
            .HasConversion(id => id.Value, value => new TransportStreamId(value))
            .HasColumnName("transport_stream_id");

        builder.Property(visit => visit.LastAttemptedAt).IsRequired();
        builder.Property(visit => visit.LastCompletedAt);

        builder.Property(visit => visit.Outcome)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(visit => visit.ConsecutiveIncomplete).IsRequired();
        builder.Property(visit => visit.LastDurationMilliseconds).IsRequired();

        builder.HasIndex(visit => visit.LastCompletedAt);
    }
}
