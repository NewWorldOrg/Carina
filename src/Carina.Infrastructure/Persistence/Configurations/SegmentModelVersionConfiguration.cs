using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

/// <summary>
/// The versions of the model. The figures an evaluation leaves — the metrics overall and by station, the
/// evidence they rest on, the threshold they recommend and what a rollback measured — are columns nothing
/// writes until versions are evaluated, so they are kept beside the entity rather than on it.
/// </summary>
public sealed class SegmentModelVersionConfiguration : IEntityTypeConfiguration<SegmentModelVersion>
{
    public const string TableName = "segment_model_version";

    public const string ActiveIndexName = "ux_segment_model_version_active";

    public static readonly IReadOnlyList<string> EvaluationColumns = ["metrics", "stations", "evidence", "recommended_threshold", "rollback"];

    private const string Candidate = nameof(SegmentModelVersionState.Candidate);

    private const string Active = nameof(SegmentModelVersionState.Active);

    private const string Retired = nameof(SegmentModelVersionState.Retired);

    public void Configure(EntityTypeBuilder<SegmentModelVersion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint("ck_segment_model_version_number", $"number >= {SegmentModelVersion.FirstNumber}");
            table.HasCheckConstraint("ck_segment_model_version_state", $"state IN ({SegmentVocabulary.Names<SegmentModelVersionState>()})");
            table.HasCheckConstraint("ck_segment_model_version_kinds", "kinds IN (2, 4)");
            table.HasCheckConstraint(
                "ck_segment_model_version_by_hand",
                $"NOT by_hand OR (state = '{Active}' AND chosen_by_hand_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_segment_model_version_times",
                $"""
                (adopted_at IS NULL) = (state = '{Candidate}')
                AND (retired_at IS NOT NULL) = (state = '{Retired}')
                AND (adopted_at IS NULL OR adopted_at >= trained_at)
                AND (retired_at IS NULL OR retired_at >= adopted_at)
                """);
            table.HasCheckConstraint(
                "ck_segment_model_version_rollback",
                $"(rolled_back_at IS NULL) = (rollback IS NULL) AND (rolled_back_at IS NULL OR state = '{Retired}')");
            table.HasCheckConstraint(
                "ck_segment_model_version_parameters",
                """
                jsonb_typeof(weights) = 'object'
                AND jsonb_typeof(station_offsets) = 'object'
                AND jsonb_typeof(series_offsets) = 'object'
                AND jsonb_typeof(thresholds) = 'object'
                AND jsonb_typeof(standardisation) = 'object'
                """);
            table.HasCheckConstraint(
                "ck_segment_model_version_evaluation",
                """
                (metrics IS NULL OR jsonb_typeof(metrics) = 'object')
                AND (stations IS NULL OR jsonb_typeof(stations) = 'array')
                AND (evidence IS NULL OR jsonb_typeof(evidence) = 'object')
                AND (rollback IS NULL OR jsonb_typeof(rollback) = 'object')
                AND (recommended_threshold IS NULL OR recommended_threshold BETWEEN 0 AND 1)
                """);
        });

        builder.HasKey(version => version.Number);

        builder.Property(version => version.Number)
            .HasColumnName("number")
            .ValueGeneratedNever();

        builder.Property(version => version.State)
            .HasConversion<string>()
            .HasMaxLength(SegmentVocabulary.NameLength)
            .IsRequired();

        builder.Property(version => version.ByHand).IsRequired();
        builder.Property(version => version.Kinds).HasColumnType("smallint").IsRequired();

        builder.Property(version => version.Weights)
            .HasConversion(weights => SegmentJson.Written(weights), stored => SegmentJson.Weights(stored), SegmentJson.ComparedAsWritten<SegmentWeights>(SegmentJson.Written))
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(version => version.StationOffsets)
            .HasConversion(offsets => SegmentJson.Written(offsets), stored => SegmentJson.Offsets(stored), SegmentJson.ComparedAsWritten<IReadOnlyDictionary<string, double>>(SegmentJson.Written))
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(version => version.SeriesOffsets)
            .HasConversion(offsets => SegmentJson.Written(offsets), stored => SegmentJson.Offsets(stored), SegmentJson.ComparedAsWritten<IReadOnlyDictionary<string, double>>(SegmentJson.Written))
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(version => version.Thresholds)
            .HasConversion(thresholds => SegmentJson.Written(thresholds), stored => SegmentJson.Thresholds(stored), SegmentJson.ComparedAsWritten<SegmentThresholds>(SegmentJson.Written))
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(version => version.Standardisation)
            .HasConversion(scales => SegmentJson.Written(scales), stored => SegmentJson.Standardisation(stored), SegmentJson.ComparedAsWritten<IReadOnlyDictionary<string, FeatureScale>>(SegmentJson.Written))
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property<string?>("Metrics").HasColumnName("metrics").HasColumnType("jsonb");
        builder.Property<string?>("Stations").HasColumnName("stations").HasColumnType("jsonb");
        builder.Property<string?>("Evidence").HasColumnName("evidence").HasColumnType("jsonb");
        builder.Property<decimal?>("RecommendedThreshold").HasColumnName("recommended_threshold").HasColumnType("numeric(3,2)");

        builder.Property(version => version.TrainedAt).IsRequired();
        builder.Property(version => version.AdoptedAt);
        builder.Property(version => version.RetiredAt);
        builder.Property(version => version.ChosenByHandAt);
        builder.Property(version => version.RolledBackAt);

        builder.Property<string?>("Rollback").HasColumnName("rollback").HasColumnType("jsonb");

        builder.Ignore(version => version.Model);

        builder.HasIndex(version => version.State)
            .IsUnique()
            .HasFilter($"state = '{Active}'")
            .HasDatabaseName(ActiveIndexName);
    }
}
