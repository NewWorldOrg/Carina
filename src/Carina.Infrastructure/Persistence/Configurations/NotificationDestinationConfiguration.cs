using Carina.Domain.Notifications;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class NotificationDestinationConfiguration : IEntityTypeConfiguration<NotificationDestination>
{
    public const string TableName = "notification_destination";

    public const string NameCheck = "ck_notification_destination_name";

    public const string TargetCheck = "ck_notification_destination_target";

    public const string SealedUrlCheck = "ck_notification_destination_sealed_url";

    public const string UrlTailCheck = "ck_notification_destination_url_tail";

    public const string LastFailureCheck = "ck_notification_destination_last_failure";

    public const string EndpointHashCheck = "ck_notification_destination_endpoint_hash";

    public const string EndpointHashIndexName = "ux_notification_destination_endpoint_hash";

    public void Configure(EntityTypeBuilder<NotificationDestination> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(NameCheck, NotificationVocabulary.AName("name"));
            table.HasCheckConstraint(TargetCheck, $"target IN ({NotificationVocabulary.Of<WebhookTarget>()})");
            table.HasCheckConstraint(
                SealedUrlCheck,
                $"octet_length(sealed_url) BETWEEN 1 AND {NotificationDestination.LargestSealedUrl}");
            table.HasCheckConstraint(UrlTailCheck, $"length(url_tail) = {NotificationDestination.UrlTailLength}");
            table.HasCheckConstraint(
                LastFailureCheck,
                $"""
                (last_failure IS NULL OR last_failure IN ({NotificationVocabulary.Of<NoticeFailure>()}))
                AND (last_failure IS NULL OR last_result_at IS NOT NULL)
                """);
            table.HasCheckConstraint(
                EndpointHashCheck,
                $$"""
                (target = '{{nameof(WebhookTarget.Push)}}') = (endpoint_hash IS NOT NULL)
                AND (endpoint_hash IS NULL OR endpoint_hash ~ '^[0-9a-f]{{{NotificationDestination.EndpointHashLength}}}$')
                """);
        });

        builder.HasKey(destination => destination.Id);

        builder.Property(destination => destination.Id)
            .HasConversion(id => id.Value, value => new NotificationDestinationId(value))
            .HasColumnName("id");

        builder.Property(destination => destination.Name)
            .HasConversion(name => name.Value, value => new NotificationName(value))
            .HasMaxLength(NotificationName.Longest)
            .IsRequired();

        builder.Property(destination => destination.Target)
            .HasConversion<string>()
            .HasMaxLength(NotificationVocabulary.NameLength)
            .IsRequired();

        builder.Property(destination => destination.SealedUrl)
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(destination => destination.UrlTail)
            .HasMaxLength(NotificationDestination.UrlTailLength)
            .IsRequired();

        builder.Property(destination => destination.EndpointHash)
            .HasMaxLength(NotificationDestination.EndpointHashLength);

        builder.HasIndex(destination => destination.EndpointHash)
            .IsUnique()
            .HasDatabaseName(EndpointHashIndexName);

        builder.Property(destination => destination.Enabled).IsRequired();

        builder.Property(destination => destination.LastResultAt);

        builder.Property(destination => destination.LastFailure)
            .HasConversion<string>()
            .HasMaxLength(NotificationVocabulary.NameLength);

        builder.Property(destination => destination.CreatedAt).IsRequired();

        builder.Property(destination => destination.ChangedAt).IsRequired();
    }
}
