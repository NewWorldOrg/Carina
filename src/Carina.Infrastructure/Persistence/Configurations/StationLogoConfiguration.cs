using Carina.Domain.Channels;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class StationLogoConfiguration : IEntityTypeConfiguration<StationLogo>
{
    public void Configure(EntityTypeBuilder<StationLogo> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("station_logo", table =>
        {
            table.HasCheckConstraint(
                "ck_station_logo_measures_something",
                $"width BETWEEN 1 AND {StationLogo.WidestPicture} AND height BETWEEN 1 AND {StationLogo.WidestPicture}");
            table.HasCheckConstraint(
                "ck_station_logo_carries_a_picture",
                $"octet_length(picture) BETWEEN 1 AND {StationLogo.LargestPicture}");
            table.HasCheckConstraint("ck_station_logo_id", $"logo_id BETWEEN {LogoId.MinValue} AND {LogoId.MaxValue}");
        });

        builder.HasKey(logo => new { logo.NetworkId, logo.LogoId });

        builder.Property(logo => logo.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id");

        builder.Property(logo => logo.LogoId)
            .HasConversion(id => id.Value, value => new LogoId(value))
            .HasColumnName("logo_id");

        builder.Property(logo => logo.LogoType).HasColumnName("logo_type").IsRequired();
        builder.Property(logo => logo.LogoVersion).HasColumnName("logo_version").IsRequired();
        builder.Property(logo => logo.Width).IsRequired();
        builder.Property(logo => logo.Height).IsRequired();
        builder.Property(logo => logo.Picture).HasColumnType("bytea").IsRequired();
        builder.Property(logo => logo.CollectedAt).IsRequired();
    }
}
