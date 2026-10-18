using System.Globalization;

using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class SegmentSettingsConfiguration : IEntityTypeConfiguration<SegmentSettings>
{
    public const string TableName = "segment_settings";

    public const string SingleRowCheck = "ck_segment_settings_single_row";

    public const string SkipThresholdCheck = "ck_segment_settings_skip_threshold";

    public void Configure(EntityTypeBuilder<SegmentSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(SingleRowCheck, $"id = {SegmentSettings.TheOnlyRow}");
            table.HasCheckConstraint(
                SkipThresholdCheck,
                $"skip_threshold BETWEEN {Number(SegmentSettingsStanding.LowestSkipThreshold)} AND {Number(SegmentSettingsStanding.HighestSkipThreshold)}");
        });

        builder.HasKey(settings => settings.Id);

        builder.Property(settings => settings.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(settings => settings.Learning).IsRequired();

        builder.Property(settings => settings.LearningChangedAt);

        builder.Property(settings => settings.SkipThreshold)
            .HasConversion<decimal>()
            .HasColumnType("numeric(3,2)")
            .HasDefaultValue(SegmentSettingsStanding.DefaultSkipThreshold)
            .IsRequired();

        builder.Property(settings => settings.SkipThresholdChangedAt);
    }

    private static string Number(double threshold) => threshold.ToString("0.00", CultureInfo.InvariantCulture);
}
