using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class ProgrammeWatchConfiguration : IEntityTypeConfiguration<ProgrammeWatch>
{
    public const string TableName = "notification_watch";

    public const string LeadCheck = "ck_notification_watch_lead_minutes";

    public const string StateCheck = "ck_notification_watch_state";

    public const string DestinationsCheck = "ck_notification_watch_destinations";

    public const string NotifiedCheck = "ck_notification_watch_notified_at";

    public const string ToldStartCheck = "ck_notification_watch_told_starts_at";

    public const string BroadcastCheck = "ck_notification_watch_broadcast";

    public const string ProgrammeIndexName = "ux_notification_watch_programme";

    public const string DueIndexName = "ix_notification_watch_state_starts_at";

    private const string Waiting = nameof(ProgrammeWatchState.Waiting);

    private const string Notified = nameof(ProgrammeWatchState.Notified);

    public void Configure(EntityTypeBuilder<ProgrammeWatch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                LeadCheck,
                $"lead_minutes IN ({string.Join(", ", NotificationSettings.LeadMinutesOffered)})");
            table.HasCheckConstraint(StateCheck, $"state IN ({NotificationVocabulary.Of<ProgrammeWatchState>()})");
            table.HasCheckConstraint(
                DestinationsCheck,
                $"jsonb_typeof(destinations) = 'array' AND (state <> '{Waiting}' OR jsonb_array_length(destinations) > 0)");
            table.HasCheckConstraint(NotifiedCheck, $"(state = '{Notified}') = (notified_at IS NOT NULL)");
            table.HasCheckConstraint(ToldStartCheck, $"(state = '{Notified}') = (told_starts_at IS NOT NULL)");
            table.HasCheckConstraint(BroadcastCheck, "ends_at > starts_at");
        });

        builder.HasKey(watch => watch.Id);

        builder.Property(watch => watch.Id)
            .HasConversion(id => id.Value, value => new ProgrammeWatchId(value))
            .HasColumnName("id");

        builder.Property(watch => watch.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id");

        builder.Property(watch => watch.ServiceId)
            .HasConversion(id => id.Value, value => new ServiceId(value))
            .HasColumnName("service_id");

        builder.Property(watch => watch.EventId)
            .HasConversion(id => id.Value, value => new EventId(value))
            .HasColumnName("event_id");

        builder.Ignore(watch => watch.Programme);

        builder.Property(watch => watch.StartsAt).IsRequired();

        builder.Property(watch => watch.EndsAt).IsRequired();

        builder.Property(watch => watch.ProgrammeName)
            .HasMaxLength(Programme.NameMaxLength)
            .IsRequired();

        builder.Property(watch => watch.StationName)
            .HasMaxLength(BroadcastService.NameMaxLength);

        builder.Property(watch => watch.LeadMinutes).IsRequired();

        builder.Property(watch => watch.Destinations)
            .HasConversion(
                destinations => JsonSerializer.Serialize(destinations.Select(id => id.Value), ProgrammeJson.Options),
                stored => ReadDestinations(stored),
                Compared())
            .HasColumnName("destinations")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(watch => watch.State)
            .HasConversion<string>()
            .HasMaxLength(NotificationVocabulary.NameLength)
            .IsRequired();

        builder.Property(watch => watch.NotifiedAt);

        builder.Property(watch => watch.ToldStartsAt);

        builder.Property(watch => watch.CreatedAt).IsRequired();

        builder.Property(watch => watch.ChangedAt).IsRequired();

        builder.HasIndex(watch => new { watch.NetworkId, watch.ServiceId, watch.EventId })
            .IsUnique()
            .HasDatabaseName(ProgrammeIndexName);

        builder.HasIndex(watch => new { watch.State, watch.StartsAt })
            .HasDatabaseName(DueIndexName);
    }

    private static IReadOnlyList<NotificationDestinationId> ReadDestinations(string stored)
        => [.. (JsonSerializer.Deserialize<List<Guid>>(stored, ProgrammeJson.Options) ?? []).Select(id => new NotificationDestinationId(id))];

    private static ValueComparer<IReadOnlyList<NotificationDestinationId>> Compared()
        => new(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            list => list.Aggregate(0, (carried, item) => HashCode.Combine(carried, item)),
            list => list.ToList());
}
