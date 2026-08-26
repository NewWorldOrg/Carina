using Carina.Domain.Channels;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class ServiceReachSettingsConfiguration : IEntityTypeConfiguration<ServiceReachSettings>
{
    public const string SingleRowCheck = "ck_service_reach_config_single_row";

    public const string SilenceCheck = "ck_service_reach_config_hours_of_silence";

    public void Configure(EntityTypeBuilder<ServiceReachSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "service_reach_config",
            table =>
            {
                table.HasCheckConstraint(
                    SingleRowCheck,
                    $"id = {ServiceReachSettings.TheOnlyRow}");
                table.HasCheckConstraint(
                    SilenceCheck,
                    $"hours_of_silence >= {ServiceReachSettings.ShortestHoursOfSilence}"
                    + $" AND hours_of_silence <= {ServiceReachSettings.LongestHoursOfSilence}");
            });

        builder.HasKey(settings => settings.Id);

        builder.Property(settings => settings.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(settings => settings.HoursOfSilence).IsRequired();

        builder.Property(settings => settings.UpdatedAt).IsRequired();

        builder.Ignore(settings => settings.Silence);
    }
}
