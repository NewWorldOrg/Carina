using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

/// <summary>
/// The rounds of learning taken, one row per day, so that a round already taken is not taken again after the
/// process starts again, each with the candidate it kept, so that a round knows what is new since the last one
/// that learned, and with what became of it, the verdict on its candidate, the figures of the version in use it was
/// judged against and that version's number, and the candidate's own figures and what they stand on, so that the
/// settings screen can say why the version in use did or did not change, and what the version in use is measured
/// by, after the candidate is let go. Rows kept before these were kept leave them empty, but for the number, which a
/// row kept before it with figures has when one version was in use at the time.
/// </summary>
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
