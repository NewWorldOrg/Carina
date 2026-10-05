using Carina.Domain.Quality;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class QualityThresholdConfiguration : IEntityTypeConfiguration<QualityThreshold>
{
    public const string TableName = "quality_threshold";

    public void Configure(EntityTypeBuilder<QualityThreshold> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_quality_threshold_key",
                $"threshold_key IN ({QualityVocabulary.Of<QualityThresholdKey>()})");
            table.HasCheckConstraint(
                "ck_quality_threshold_standing",
                """
                observations >= 0
                AND (provisional OR observations > 0)
                """);
            table.HasCheckConstraint(
                "ck_quality_threshold_measurement",
                """
                (measured_value IS NULL) = (measured_sessions IS NULL)
                AND (measured_value IS NULL) = (measured_sessions_dropped IS NULL)
                AND (measured_value IS NULL) = (measured_from IS NULL)
                AND (measured_value IS NULL) = (measured_until IS NULL)
                AND (measured_value IS NULL) = (measured_at IS NULL)
                AND (measured_sessions IS NULL OR measured_sessions > 0)
                AND (measured_sessions_dropped IS NULL
                    OR (measured_sessions_dropped >= 0 AND measured_sessions_dropped <= measured_sessions))
                AND (measured_from IS NULL OR measured_from <= measured_until)
                """);
            table.HasCheckConstraint(
                "ck_quality_threshold_source",
                """
                (NOT by_hand OR provisional)
                AND (provisional OR current_value = measured_value)
                """);
        });

        builder.HasKey(threshold => threshold.Key);

        builder.Property(threshold => threshold.Key)
            .HasConversion<string>()
            .HasColumnName("threshold_key")
            .HasMaxLength(QualityVocabulary.NameLength);

        builder.ComplexProperty(threshold => threshold.Setting, setting =>
        {
            setting.Property(value => value.Default).HasColumnName("default_value");
            setting.Property(value => value.Current).HasColumnName("current_value");
            setting.Property(value => value.Provisional).HasColumnName("provisional");
            setting.Property(value => value.Observations).HasColumnName("observations");
            setting.Property(value => value.UpdatedAt).HasColumnName("updated_at");

            setting.Ignore(value => value.IsAsShipped);
        });

        builder.Property(threshold => threshold.UpdatedBy)
            .HasMaxLength(QualityThreshold.UpdatedByMaxLength);

        builder.Property(threshold => threshold.ByHand).IsRequired();

        builder.Ignore(threshold => threshold.Measurement);
    }
}
