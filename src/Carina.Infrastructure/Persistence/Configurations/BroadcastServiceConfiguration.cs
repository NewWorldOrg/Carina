using Carina.Domain.Channels;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class BroadcastServiceConfiguration : IEntityTypeConfiguration<BroadcastService>
{
    public void Configure(EntityTypeBuilder<BroadcastService> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "broadcast_service",
            table => table.HasCheckConstraint(
                "ck_broadcast_service_category",
                "category IN ('Television', 'Radio', 'Data', 'OneSeg', 'Temporary', 'Other')"));

        builder.HasKey(service => new { service.NetworkId, service.ServiceId });

        builder.Property(service => service.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id");

        builder.Property(service => service.ServiceId)
            .HasConversion(id => id.Value, value => new ServiceId(value))
            .HasColumnName("service_id");

        builder.Property(service => service.Name)
            .HasMaxLength(BroadcastService.NameMaxLength)
            .IsRequired();

        builder.Property(service => service.Category)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(service => service.RemoteControlKeyId)
            .HasColumnName("remote_control_key_id");

        builder.Property(service => service.DiscoveredAt).IsRequired();
        builder.Property(service => service.LastSeenAt).IsRequired();

        builder.HasIndex(service => service.LastSeenAt);
    }
}
