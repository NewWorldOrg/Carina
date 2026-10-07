using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class LearningExtractionConfiguration : IEntityTypeConfiguration<LearningExtraction>
{
    public const string TableName = "segment_extraction";

    public const string ReadingIndexName = "ux_segment_extraction_reading";

    public const string ConcurrencyToken = "xmin";

    private const string Waiting = nameof(LearningExtractionState.Waiting);

    private const string Reading = nameof(LearningExtractionState.Reading);

    private const string Done = nameof(LearningExtractionState.Done);

    private const string Partial = nameof(LearningExtractionState.Partial);

    private const string Failed = nameof(LearningExtractionState.Failed);

    public void Configure(EntityTypeBuilder<LearningExtraction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_segment_extraction_state",
                $"state IN ({SegmentVocabulary.Names<LearningExtractionState>()})");
            table.HasCheckConstraint(
                "ck_segment_extraction_version",
                $"""
                (version_number IS NULL) = (version_origin IS NULL)
                AND (version_number IS NULL OR version_number >= 1)
                AND (version_origin IS NULL OR version_origin IN ({SegmentVocabulary.Names<ExtractionOrigin>()}))
                AND (version_number IS NOT NULL OR state = '{Waiting}')
                """);
            table.HasCheckConstraint(
                "ck_segment_extraction_progress",
                """
                read_through >= interval '0'
                AND jsonb_typeof(gaps) = 'array'
                AND (sound_stream IS NULL) = (sound_offset IS NULL)
                AND (sound_stream IS NULL OR sound_stream >= 0)
                """);
            table.HasCheckConstraint(
                "ck_segment_extraction_failure",
                $"""
                failures >= 0
                AND (failure IS NULL) = (failure_reason IS NULL)
                AND (failure IS NULL) = (failures = 0)
                AND (failure IS NULL OR failure IN ({SegmentVocabulary.Names<ExtractionFailure>()}))
                AND (state <> '{Failed}' OR failure IS NOT NULL)
                AND (state NOT IN ('{Done}', '{Partial}') OR failures = 0)
                """);
            table.HasCheckConstraint(
                "ck_segment_extraction_programme",
                $"""
                (programme_ends_at IS NULL OR programme_ends_at > programme_starts_at)
                AND (episode IS NULL OR episode >= 0)
                AND audio IN ({SegmentVocabulary.Names<AudioMode>()})
                AND jsonb_typeof(genres) = 'array'
                AND jsonb_typeof(marks) = 'array'
                """);
            table.HasCheckConstraint("ck_segment_extraction_times", "updated_at >= created_at");
        });

        builder.Property<uint>(ConcurrencyToken)
            .HasColumnName(ConcurrencyToken)
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasKey(extraction => extraction.RecordingId);

        builder.Property(extraction => extraction.RecordingId)
            .HasConversion(id => id.Value, value => new RecordingId(value))
            .HasColumnName("recording_id")
            .ValueGeneratedNever();

        builder.Property(extraction => extraction.State)
            .HasConversion<string>()
            .HasMaxLength(SegmentVocabulary.NameLength)
            .IsRequired();

        builder.ComplexProperty(extraction => extraction.Version, version =>
        {
            version.Property(detail => detail.Number).HasColumnName("version_number");
            version.Property(detail => detail.Origin)
                .HasConversion<string>()
                .HasColumnName("version_origin")
                .HasMaxLength(SegmentVocabulary.NameLength);
        });

        builder.Property(extraction => extraction.ReadThrough).HasColumnName("read_through").IsRequired();

        builder.Property(extraction => extraction.Gaps)
            .HasConversion(
                gaps => SegmentVocabulary.Written(gaps),
                stored => SegmentVocabulary.Read<LearningDataGap>(stored),
                SegmentVocabulary.Compared<LearningDataGap>())
            .HasColumnName("gaps")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.ComplexProperty(extraction => extraction.Sound, sound =>
        {
            sound.Property(detail => detail.Stream).HasColumnName("sound_stream");
            sound.Property(detail => detail.Offset).HasColumnName("sound_offset");
        });

        builder.ComplexProperty(extraction => extraction.Failure, failure =>
        {
            failure.Property(detail => detail.Failure)
                .HasConversion<string>()
                .HasColumnName("failure")
                .HasMaxLength(SegmentVocabulary.NameLength);
            failure.Property(detail => detail.Reason)
                .HasColumnName("failure_reason")
                .HasMaxLength(ExtractionFailureDetail.LongestReason);
        });

        builder.Property(extraction => extraction.Failures).IsRequired();

        builder.ComplexProperty(extraction => extraction.Programme, programme =>
        {
            programme.Property(copy => copy.NetworkId)
                .HasConversion(id => id.Value, value => new NetworkId(value))
                .HasColumnName("network_id");
            programme.Property(copy => copy.ServiceId)
                .HasConversion(id => id.Value, value => new ServiceId(value))
                .HasColumnName("service_id");
            programme.Property(copy => copy.ProgrammeStartsAt).HasColumnName("programme_starts_at");
            programme.Property(copy => copy.ProgrammeEndsAt).HasColumnName("programme_ends_at");
            programme.Property(copy => copy.RecordingStartedAt).HasColumnName("recording_started_at");
            programme.Property(copy => copy.Name)
                .HasColumnName("name")
                .HasMaxLength(Reservation.NameMaxLength);
            programme.Property(copy => copy.Genres)
                .HasConversion(
                    genres => SegmentVocabulary.Written(genres),
                    stored => SegmentVocabulary.Read<ProgrammeGenre>(stored),
                    SegmentVocabulary.Compared<ProgrammeGenre>())
                .HasColumnName("genres")
                .HasColumnType("jsonb");
            programme.Property(copy => copy.Marks)
                .HasConversion(
                    marks => SegmentVocabulary.Written(marks),
                    stored => SegmentVocabulary.Read<ProgrammeMark>(stored),
                    SegmentVocabulary.Compared<ProgrammeMark>())
                .HasColumnName("marks")
                .HasColumnType("jsonb");
            programme.Property(copy => copy.Episode).HasColumnName("episode");
            programme.Property(copy => copy.Audio)
                .HasConversion<string>()
                .HasColumnName("audio")
                .HasMaxLength(SegmentVocabulary.NameLength);
            programme.Property(copy => copy.SeriesName)
                .HasColumnName("series_name")
                .HasMaxLength(Reservation.NameMaxLength);
        });

        builder.Property(extraction => extraction.CreatedAt).IsRequired();
        builder.Property(extraction => extraction.UpdatedAt).IsRequired();

        builder.Ignore(extraction => extraction.CanRetry);

        builder.HasIndex(extraction => extraction.State)
            .IsUnique()
            .HasFilter($"state = '{Reading}'")
            .HasDatabaseName(ReadingIndexName);
    }
}
