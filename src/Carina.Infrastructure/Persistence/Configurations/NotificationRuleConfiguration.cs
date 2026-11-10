using System.Text.Json;

using Carina.Domain.Notifications;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class NotificationRuleConfiguration : IEntityTypeConfiguration<NotificationRule>
{
    public const string TableName = "notification_rule";

    public const string NameCheck = "ck_notification_rule_name";

    public const string EventsCheck = "ck_notification_rule_events";

    public const string DestinationsCheck = "ck_notification_rule_destinations";

    public void Configure(EntityTypeBuilder<NotificationRule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(NameCheck, NotificationVocabulary.AName("name"));
            table.HasCheckConstraint(
                EventsCheck,
                $"jsonb_typeof(events) = 'array' AND events <@ '{NotificationVocabulary.Listed<NoticeKind>()}'::jsonb");
            table.HasCheckConstraint(
                DestinationsCheck,
                "jsonb_typeof(destinations) = 'array' AND jsonb_array_length(destinations) > 0");
        });

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Id)
            .HasConversion(id => id.Value, value => new NotificationRuleId(value))
            .HasColumnName("id");

        builder.Property(rule => rule.Name)
            .HasConversion(name => name.Value, value => new NotificationName(value))
            .HasMaxLength(NotificationName.Longest)
            .IsRequired();

        builder.Property(rule => rule.Enabled).IsRequired();

        builder.Property(rule => rule.Events)
            .HasConversion(
                events => JsonSerializer.Serialize(events, ProgrammeJson.Options),
                stored => ReadEvents(stored),
                Compared<NoticeKind>())
            .HasColumnName("events")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(rule => rule.Destinations)
            .HasConversion(
                destinations => JsonSerializer.Serialize(destinations.Select(id => id.Value), ProgrammeJson.Options),
                stored => ReadDestinations(stored),
                Compared<NotificationDestinationId>())
            .HasColumnName("destinations")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(rule => rule.CreatedAt).IsRequired();

        builder.Property(rule => rule.ChangedAt).IsRequired();
    }

    private static IReadOnlyList<NoticeKind> ReadEvents(string stored)
        => JsonSerializer.Deserialize<List<NoticeKind>>(stored, ProgrammeJson.Options) ?? [];

    private static IReadOnlyList<NotificationDestinationId> ReadDestinations(string stored)
        => [.. (JsonSerializer.Deserialize<List<Guid>>(stored, ProgrammeJson.Options) ?? []).Select(id => new NotificationDestinationId(id))];

    private static ValueComparer<IReadOnlyList<T>> Compared<T>()
        where T : notnull
        => new(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            list => list.Aggregate(0, (carried, item) => HashCode.Combine(carried, item)),
            list => list.ToList());
}
