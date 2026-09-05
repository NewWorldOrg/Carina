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
    }
}
