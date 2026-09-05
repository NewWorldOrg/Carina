using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class EncodeJobConfiguration : IEntityTypeConfiguration<EncodeJob>
{
    public const string QueuedIndexName = "ix_encode_job_queued";

    public const string RunningIndexName = "ux_encode_job_running";

    public const string ArtefactIndexName = "ux_encode_job_artefact";

    public const string RecordingIndexName = "ix_encode_job_recording";

    public const string ConcurrencyToken = "xmin";

    public void Configure(EntityTypeBuilder<EncodeJob> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("encode_job", table =>
        {
            table.HasCheckConstraint("ck_encode_job_status", $"status IN ({EncodeVocabulary.Of<EncodeJobStatus>()})");
            table.HasCheckConstraint("ck_encode_job_attempt", $"attempt >= {EncodeJob.FirstAttempt}");
            table.HasCheckConstraint("ck_encode_job_output_root", EncodeVocabulary.ASingleName("output_root"));
            table.HasCheckConstraint(
                "ck_encode_job_timeline",
                """
                ((status = 'Queued') = (started_at IS NULL AND ended_at IS NULL))
                AND ((status = 'Running') = (started_at IS NOT NULL AND ended_at IS NULL))
                AND ((status IN ('Completed', 'Failed', 'Cancelled')) = (ended_at IS NOT NULL))
                AND (started_at IS NULL OR started_at >= queued_at)
                AND (ended_at IS NULL OR ended_at >= queued_at)
                AND (ended_at IS NULL OR started_at IS NULL OR ended_at >= started_at)
                """);
            table.HasCheckConstraint(
                "ck_encode_job_failure",
                $"""
                ((status = 'Failed') = (failure IS NOT NULL))
                AND ((failure IS NULL) = (failure_note IS NULL))
                AND ((failure IS NULL) = (failure_noticed_at IS NULL))
                AND (failure IS NULL OR failure IN ({EncodeVocabulary.Of<EncodeFailure>()}))
                """);
            table.HasCheckConstraint(
                "ck_encode_job_programme",
                """
                ((process_id IS NULL) = (process_started_at IS NULL))
                AND (process_id IS NULL OR status = 'Running')
                AND (process_id IS NULL OR process_id >= 1)
                AND (process_started_at IS NULL OR process_started_at >= started_at)
                """);
            table.HasCheckConstraint(
                "ck_encode_job_headway",
                """
                (progress_at IS NULL OR status <> 'Queued')
                AND (progress_at IS NULL OR progress_at >= started_at)
                AND (progress_at IS NOT NULL OR (progress_portion IS NULL AND progress_left IS NULL))
                AND (progress_portion IS NULL OR progress_portion BETWEEN 0 AND 1)
                AND (progress_left IS NULL OR progress_left >= interval '0')
                """);
            table.HasCheckConstraint(
                "ck_encode_job_route",
                $"""
                ((encoder_asked IS NULL) = (encoder_ran IS NULL))
                AND (encoder_asked IS NULL OR status <> 'Queued')
                AND (encoder_asked IS NULL OR encoder_asked IN ({EncodeVocabulary.Of<EncodeEncoder>()}))
                AND (encoder_ran IS NULL OR encoder_ran IN ({EncodeVocabulary.Of<EncodeEncoder>()}))
                AND (swerve IS NULL OR swerve IN ({EncodeVocabulary.Of<EncodeSwerve>()}))
                AND (encoder_asked IS NULL OR ((swerve IS NULL) = (encoder_asked = encoder_ran)))
                """);
            table.HasCheckConstraint(
                "ck_encode_job_alignment",
                $"""
                ((head_skip IS NULL) = (source_start IS NULL))
                AND (head_skip IS NULL OR status <> 'Queued')
                AND (head_skip IS NULL OR head_skip BETWEEN interval '0' AND interval '{EncodeTimeline.MostHeadSkip.TotalSeconds:0} seconds')
                AND (source_start IS NULL OR source_start >= interval '0')
                AND (source_length IS NULL OR (head_skip IS NOT NULL AND source_length > interval '0'))
                AND (artefact_length IS NULL OR (head_skip IS NOT NULL AND artefact_length >= interval '0'))
                """);
            table.HasCheckConstraint(
                "ck_encode_job_artefact",
                $"""
                (status <> 'Completed' OR artefact_name IS NOT NULL)
                AND (artefact_name IS NULL
                    OR ({EncodeVocabulary.ASingleName("artefact_name")}
                        AND strpos(artefact_name, replace(recording_id::text, '-', '')) > 0
                        AND strpos(artefact_name, replace(profile_id::text, '-', '')) > 0))
                """);
        });

        builder.Property<uint>(ConcurrencyToken)
            .HasColumnName(ConcurrencyToken)
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasKey(job => job.Id);

        builder.Property(job => job.Id)
            .HasConversion(id => id.Value, value => new EncodeJobId(value))
            .HasColumnName("id");

        builder.Property(job => job.RecordingId)
            .HasConversion(id => id.Value, value => new RecordingId(value))
            .HasColumnName("recording_id")
            .IsRequired();

        builder.Property(job => job.ProfileId)
            .HasConversion(id => id.Value, value => new EncodeProfileId(value))
            .HasColumnName("profile_id")
            .IsRequired();

        builder.Property(job => job.DestinationId)
            .HasConversion(id => id.Value, value => new EncodeDestinationId(value))
            .HasColumnName("destination_id")
            .IsRequired();

        builder.Property(job => job.OutputRoot)
            .HasConversion(root => root.Value, value => new OutputRoot(value))
            .HasMaxLength(Carina.Domain.Recordings.OutputRoot.MaxLength)
            .IsRequired();

        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(job => job.Attempt).IsRequired();
        builder.Property(job => job.QueuedAt).IsRequired();
        builder.Property(job => job.StartedAt);
        builder.Property(job => job.EndedAt);

        builder.ComplexProperty(job => job.Failure, failure =>
        {
            failure.Property(detail => detail.Failure)
                .HasConversion<string>()
                .HasColumnName("failure")
                .HasMaxLength(32);

            failure.Property(detail => detail.Note)
                .HasColumnName("failure_note")
                .HasMaxLength(EncodeNote.Longest);

            failure.Property(detail => detail.NoticedAt)
                .HasColumnName("failure_noticed_at");
        });

        builder.Property(job => job.ArtefactName)
            .HasConversion(name => name!.Value, value => new EncodeFileName(value))
            .HasMaxLength(EncodeFileName.MaxLength);

        builder.ComplexProperty(job => job.Route, route =>
        {
            route.Property(detail => detail.Asked)
                .HasConversion<string>()
                .HasColumnName("encoder_asked")
                .HasMaxLength(32);

            route.Property(detail => detail.Ran)
                .HasConversion<string>()
                .HasColumnName("encoder_ran")
                .HasMaxLength(32);

            route.Property(detail => detail.Swerved)
                .HasConversion<string>()
                .HasColumnName("swerve")
                .HasMaxLength(32);

            route.Ignore(detail => detail.WasDegraded);
        });

        builder.ComplexProperty(job => job.Programme, programme =>
        {
            programme.Property(detail => detail.ProcessId).HasColumnName("process_id");
            programme.Property(detail => detail.StartedAt).HasColumnName("process_started_at");
        });

        builder.ComplexProperty(job => job.Headway, headway =>
        {
            headway.Property(detail => detail.Portion).HasColumnName("progress_portion");
            headway.Property(detail => detail.Left).HasColumnName("progress_left");
            headway.Property(detail => detail.At).HasColumnName("progress_at");
        });

        builder.ComplexProperty(job => job.Timeline, timeline =>
        {
            timeline.Property(detail => detail.SourceStart).HasColumnName("source_start");
            timeline.Property(detail => detail.HeadSkip).HasColumnName("head_skip");
            timeline.Property(detail => detail.SourceLength).HasColumnName("source_length");
            timeline.Property(detail => detail.ArtefactLength).HasColumnName("artefact_length");

            timeline.Ignore(detail => detail.CaptionShift);
            timeline.Ignore(detail => detail.Expected);
            timeline.Ignore(detail => detail.Drift);
            timeline.Ignore(detail => detail.LengthsAgree);
        });

        builder.Ignore(job => job.HasEnded);
        builder.Ignore(job => job.Standing);
        builder.Ignore(job => job.WorkFileName);

        builder.HasOne<EncodeProfile>()
            .WithMany()
            .HasForeignKey(job => job.ProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EncodeDestination>()
            .WithMany()
            .HasForeignKey(job => job.DestinationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(job => job.QueuedAt)
            .HasFilter("status = 'Queued'")
            .HasDatabaseName(QueuedIndexName);

        builder.HasIndex(job => job.Status)
            .IsUnique()
            .HasFilter("status = 'Running'")
            .HasDatabaseName(RunningIndexName);

        builder.HasIndex(job => new { job.OutputRoot, job.ArtefactName })
            .IsUnique()
            .HasFilter("artefact_name IS NOT NULL")
            .HasDatabaseName(ArtefactIndexName);

        builder.HasIndex(job => new { job.RecordingId, job.QueuedAt })
            .HasDatabaseName(RecordingIndexName);
    }
}
