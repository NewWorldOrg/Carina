using Carina.Domain.Quality;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class QualityThresholdChangeConfiguration : IEntityTypeConfiguration<QualityThresholdChange>
{
    public const string TableName = "quality_threshold_change";

    public const string HistoryIndexName = "ix_quality_threshold_change_history";

    public void Configure(EntityTypeBuilder<QualityThresholdChange> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table => table.HasCheckConstraint(
            "ck_quality_threshold_change_key",
            $"threshold_key IN ({QualityVocabulary.Of<QualityThresholdKey>()})"));

        builder.HasKey(change => change.Id);

        builder.Property(change => change.Id)
            .HasConversion(id => id.Value, stored => new QualityThresholdChangeId(stored))
            .HasColumnName("id");

        builder.Property(change => change.Key)
            .HasConversion<string>()
            .HasColumnName("threshold_key")
            .HasMaxLength(QualityVocabulary.NameLength)
            .IsRequired();

        builder.Property(change => change.PreviousValue).IsRequired();
        builder.Property(change => change.NextValue).IsRequired();
        builder.Property(change => change.ChangedAt).IsRequired();

        builder.Property(change => change.ChangedBy)
            .HasMaxLength(QualityThresholdChange.ChangedByMaxLength);

        builder.HasIndex(change => new { change.Key, change.ChangedAt })
            .HasDatabaseName(HistoryIndexName);
    }
}
