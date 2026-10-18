using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

/// <summary>
/// The rounds of learning taken, one row per day, so that a round already taken is not taken again after the
/// process starts again.
/// </summary>
public sealed class SegmentLearningRoundConfiguration : IEntityTypeConfiguration<SegmentLearningRound>
{
    public const string TableName = "segment_learning_round";

    public void Configure(EntityTypeBuilder<SegmentLearningRound> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(round => round.Day);

        builder.Property(round => round.Day)
            .HasColumnName("day")
            .HasColumnType("date")
            .ValueGeneratedNever();

        builder.Property(round => round.TakenAt).IsRequired();
    }
}
