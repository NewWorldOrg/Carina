using Carina.Domain.DataBroadcast;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class DataBroadcastSettingsConfiguration : IEntityTypeConfiguration<DataBroadcastSettings>
{
    public const string TableName = "data_broadcast_settings";

    public const string SingleRowCheck = "ck_data_broadcast_settings_single_row";

    public const string PostalCodeCheck = "ck_data_broadcast_settings_postal_code";

    public const string PrefectureCheck = "ck_data_broadcast_settings_prefecture";

    public void Configure(EntityTypeBuilder<DataBroadcastSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(SingleRowCheck, $"id = {DataBroadcastSettings.TheOnlyRow}");
            table.HasCheckConstraint(
                PostalCodeCheck,
                $"postal_code ~ '^[0-9]{{{DataBroadcastSettingsStanding.PostalCodeLength}}}$'");
            table.HasCheckConstraint(
                PrefectureCheck,
                $"prefecture BETWEEN {DataBroadcastSettingsStanding.FirstPrefecture} AND {DataBroadcastSettingsStanding.LastPrefecture}");
        });

        builder.HasKey(settings => settings.Id);

        builder.Property(settings => settings.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(settings => settings.PostalCode)
            .HasMaxLength(DataBroadcastSettingsStanding.PostalCodeLength);

        builder.Property(settings => settings.Prefecture)
            .HasColumnType("smallint");

        builder.Property(settings => settings.ChangedAt)
            .IsRequired();
    }
}
