using Carina.Domain.Notifications;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class NotificationSettingsConfiguration : IEntityTypeConfiguration<NotificationSettings>
{
    public const string TableName = "notification_settings";

    public const string SingleRowCheck = "ck_notification_settings_single_row";

    public const string LeadCheck = "ck_notification_settings_notify_lead_minutes";

    public void Configure(EntityTypeBuilder<NotificationSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(SingleRowCheck, $"id = {NotificationSettings.TheOnlyRow}");
            table.HasCheckConstraint(
                LeadCheck,
                $"notify_lead_minutes IN ({string.Join(", ", NotificationSettings.LeadMinutesOffered)})");
        });

        builder.HasKey(settings => settings.Id);

        builder.Property(settings => settings.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(settings => settings.NotifyLeadMinutes).IsRequired();

        builder.Property(settings => settings.ChangedAt).IsRequired();
    }
}
