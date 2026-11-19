using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class SegmentLearningRoundConfiguration : IEntityTypeConfiguration<SegmentLearningRound>
{
    public const string TableName = "segment_learning_round";

    private const string Stopped = nameof(SegmentTrainingOutcome.Stopped);

    public void Configure(EntityTypeBuilder<SegmentLearningRound> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_segment_learning_round_outcome",
                $"outcome IN ({SegmentVocabulary.Names<SegmentTrainingOutcome>()}) AND outcome <> '{Stopped}'");
            table.HasCheckConstraint("ck_segment_learning_round_verdict", $"verdict IN ({SegmentVocabulary.Names<AdoptionVerdict>()})");
            table.HasCheckConstraint(
                "ck_segment_learning_round_judged",
                "(candidate IS NOT NULL OR (verdict IS NULL AND in_use_metrics IS NULL AND in_use_version IS NULL)) AND (in_use_metrics IS NULL OR jsonb_typeof(in_use_metrics) = 'object')");
            table.HasCheckConstraint("ck_segment_learning_round_in_use_version", $"in_use_version >= {SegmentModelVersion.FirstNumber}");
            table.HasCheckConstraint(
                "ck_segment_learning_round_candidate_figures",
                """
                (candidate IS NOT NULL OR (candidate_metrics IS NULL AND candidate_evidence IS NULL))
                AND (candidate_metrics IS NULL OR jsonb_typeof(candidate_metrics) = 'object')
                AND (candidate_evidence IS NULL OR jsonb_typeof(candidate_evidence) = 'object')
                """);
        });

        builder.HasKey(round => round.Day);

        builder.Property(round => round.Day)
            .HasColumnName("day")
            .HasColumnType("date")
            .ValueGeneratedNever();

        builder.Property(round => round.TakenAt).IsRequired();

        builder.Property(round => round.Candidate).HasColumnName("candidate");

        builder.Property(round => round.Outcome)
            .HasColumnName("outcome")
            .HasConversion<string>()
            .HasMaxLength(SegmentVocabulary.NameLength);

        builder.Property(round => round.Verdict)
            .HasColumnName("verdict")
            .HasConversion<string>()
            .HasMaxLength(SegmentVocabulary.NameLength);

        builder.Property(round => round.InUseMetrics)
            .HasConversion(metrics => SegmentJson.Written(metrics!), stored => SegmentJson.Metrics(stored), SegmentJson.ComparedAsWritten<SegmentMetrics>(SegmentJson.Written))
            .HasColumnName("in_use_metrics")
            .HasColumnType("jsonb");

        builder.Property(round => round.InUseVersion).HasColumnName("in_use_version");

        builder.Property(round => round.CandidateMetrics)
            .HasConversion(metrics => SegmentJson.Written(metrics!), stored => SegmentJson.Metrics(stored), SegmentJson.ComparedAsWritten<SegmentMetrics>(SegmentJson.Written))
            .HasColumnName("candidate_metrics")
            .HasColumnType("jsonb");

        builder.Property(round => round.CandidateEvidence)
            .HasConversion(evidence => SegmentJson.Written(evidence!), stored => SegmentJson.Evidence(stored), SegmentJson.ComparedAsWritten<EvaluationEvidence>(SegmentJson.Written))
            .HasColumnName("candidate_evidence")
            .HasColumnType("jsonb");
    }
}
