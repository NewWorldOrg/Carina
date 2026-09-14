using Carina.Domain.Encodings;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class EncodeChapterConfiguration : IEntityTypeConfiguration<EncodeChapter>
{
    public const string OrdinalIndexName = "ux_encode_chapter_ordinal";

    public void Configure(EntityTypeBuilder<EncodeChapter> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("encode_chapter", table =>
        {
            table.HasCheckConstraint("ck_encode_chapter_kind", $"kind IN ({EncodeVocabulary.Of<ChapterKind>()})");
            table.HasCheckConstraint("ck_encode_chapter_ordinal", $"ordinal >= {EncodeChapter.FirstOrdinal}");
            table.HasCheckConstraint(
                "ck_encode_chapter_span",
                "starts_at >= interval '0' AND ends_at > starts_at");
        });

        builder.HasKey(chapter => chapter.Id);

        builder.Property(chapter => chapter.Id)
            .HasConversion(id => id.Value, value => new EncodeChapterId(value))
            .HasColumnName("id");

        builder.Property(chapter => chapter.JobId)
            .HasConversion(id => id.Value, value => new EncodeJobId(value))
            .HasColumnName("job_id")
            .IsRequired();

        builder.Property(chapter => chapter.Ordinal).IsRequired();
        builder.Property(chapter => chapter.StartsAt).IsRequired();
        builder.Property(chapter => chapter.EndsAt).IsRequired();
        builder.Property(chapter => chapter.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Ignore(chapter => chapter.Segment);

        builder.HasOne<EncodeJob>()
            .WithMany()
            .HasForeignKey(chapter => chapter.JobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(chapter => new { chapter.JobId, chapter.Ordinal })
            .IsUnique()
            .HasDatabaseName(OrdinalIndexName);
    }
}
