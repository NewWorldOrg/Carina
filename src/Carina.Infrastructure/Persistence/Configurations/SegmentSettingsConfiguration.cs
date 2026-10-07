using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class SegmentSettingsConfiguration : IEntityTypeConfiguration<SegmentSettings>
{
    public const string TableName = "segment_settings";

    public const string SingleRowCheck = "ck_segment_settings_single_row";

    public void Configure(EntityTypeBuilder<SegmentSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table => table.HasCheckConstraint(SingleRowCheck, $"id = {SegmentSettings.TheOnlyRow}"));

        builder.HasKey(settings => settings.Id);

        builder.Property(settings => settings.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(settings => settings.Learning).IsRequired();

        builder.Property(settings => settings.LearningChangedAt).IsRequired();
    }
}
