using Carina.Domain.Notifications;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class NoticeConfiguration : IEntityTypeConfiguration<Notice>
{
    public const string TableName = "notification_event";

    public const string SourceIndexName = "ux_notification_event_source_kind";

    public const string OccurrenceIndexName = "ix_notification_event_occurred_at";

    public const string KindCheck = "ck_notification_event_kind";

    public const string ToneCheck = "ck_notification_event_tone";

    public const string WordsCheck = "ck_notification_event_words";

    public void Configure(EntityTypeBuilder<Notice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(KindCheck, $"kind IN ({NotificationVocabulary.Of<NoticeKind>()})");
            table.HasCheckConstraint(ToneCheck, $"tone IN ({NotificationVocabulary.Of<NoticeTone>()})");
            table.HasCheckConstraint(
                WordsCheck,
                "length(btrim(title)) > 0 AND length(btrim(body)) > 0 AND (link IS NULL OR link ~ '^https?://')");
        });

        builder.HasKey(notice => notice.Id);

        builder.Property(notice => notice.Id)
            .HasConversion(id => id.Value, value => new NoticeId(value))
            .HasColumnName("id");

        builder.Property(notice => notice.Kind)
            .HasConversion<string>()
            .HasMaxLength(NotificationVocabulary.NameLength)
            .IsRequired();

        builder.Property(notice => notice.SourceId).IsRequired();

        builder.Property(notice => notice.OccurredAt).IsRequired();

        builder.Property(notice => notice.Tone)
            .HasConversion<string>()
            .HasMaxLength(NotificationVocabulary.NameLength)
            .IsRequired();

        builder.Property(notice => notice.Title)
            .HasMaxLength(Notice.TitleLongest)
            .IsRequired();

        builder.Property(notice => notice.Body)
            .HasMaxLength(Notice.BodyLongest)
            .IsRequired();

        builder.Property(notice => notice.Link)
            .HasMaxLength(Notice.LinkLongest);

        builder.Property(notice => notice.TakenAt).IsRequired();

        builder.HasIndex(notice => new { notice.SourceId, notice.Kind })
            .HasDatabaseName(SourceIndexName)
            .IsUnique();

        builder.HasIndex(notice => notice.OccurredAt).HasDatabaseName(OccurrenceIndexName);
    }
}
