using Carina.Domain.Notifications;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public const string TableName = "notification_delivery";

    public const string OncePerDestinationIndexName = "ux_notification_delivery_event_destination";

    public const string WaitingIndexName = "ix_notification_delivery_next_attempt_at";

    public const string StateCheck = "ck_notification_delivery_state";

    public const string AttemptsCheck = "ck_notification_delivery_attempts";

    public const string LastFailureCheck = "ck_notification_delivery_last_failure";

    public const string StandingCheck = "ck_notification_delivery_standing";

    private const string Queued = nameof(NotificationDeliveryState.Queued);

    private const string Failed = nameof(NotificationDeliveryState.Failed);

    private const string Sent = nameof(NotificationDeliveryState.Sent);

    private const string GivenUp = nameof(NotificationDeliveryState.GivenUp);

    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(StateCheck, $"state IN ({NotificationVocabulary.Of<NotificationDeliveryState>()})");
            table.HasCheckConstraint(AttemptsCheck, $"attempts BETWEEN 0 AND {NoticeRetry.MostAttempts}");
            table.HasCheckConstraint(
                LastFailureCheck,
                $"last_failure IS NULL OR last_failure IN ({NotificationVocabulary.Of<NoticeFailure>()})");
            table.HasCheckConstraint(
                StandingCheck,
                $"""
                (state <> '{Queued}'
                 OR (attempts = 0 AND next_attempt_at IS NOT NULL AND sent_at IS NULL AND last_failure IS NULL))
                AND (state <> '{Failed}'
                     OR (attempts > 0 AND next_attempt_at IS NOT NULL AND sent_at IS NULL AND last_failure IS NOT NULL))
                AND (state <> '{Sent}'
                     OR (attempts > 0 AND next_attempt_at IS NULL AND sent_at IS NOT NULL AND last_failure IS NULL))
                AND (state <> '{GivenUp}'
                     OR (next_attempt_at IS NULL AND sent_at IS NULL AND last_failure IS NOT NULL))
                """);
        });

        builder.HasKey(delivery => delivery.Id);

        builder.Property(delivery => delivery.Id)
            .HasConversion(id => id.Value, value => new NotificationDeliveryId(value))
            .HasColumnName("id");

        builder.Property(delivery => delivery.NoticeId)
            .HasConversion(id => id.Value, value => new NoticeId(value))
            .HasColumnName("event_id")
            .IsRequired();

        builder.Property(delivery => delivery.DestinationId)
            .HasConversion(id => id.Value, value => new NotificationDestinationId(value))
            .HasColumnName("destination_id")
            .IsRequired();

        builder.Property(delivery => delivery.State)
            .HasConversion<string>()
            .HasMaxLength(NotificationVocabulary.NameLength)
            .IsRequired();

        builder.Property(delivery => delivery.Attempts).IsRequired();

        builder.Property(delivery => delivery.NextAttemptAt);

        builder.Property(delivery => delivery.SentAt);

        builder.Property(delivery => delivery.LastFailure)
            .HasConversion<string>()
            .HasMaxLength(NotificationVocabulary.NameLength);

        builder.Property(delivery => delivery.QueuedAt).IsRequired();

        builder.HasOne<Notice>()
            .WithMany()
            .HasForeignKey(delivery => delivery.NoticeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<NotificationDestination>()
            .WithMany()
            .HasForeignKey(delivery => delivery.DestinationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(delivery => new { delivery.NoticeId, delivery.DestinationId })
            .HasDatabaseName(OncePerDestinationIndexName)
            .IsUnique();

        builder.HasIndex(delivery => delivery.NextAttemptAt)
            .HasDatabaseName(WaitingIndexName)
            .HasFilter("next_attempt_at IS NOT NULL");
    }
}
