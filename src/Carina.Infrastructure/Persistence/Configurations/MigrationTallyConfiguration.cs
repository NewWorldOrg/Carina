using Carina.Domain.Migration;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class MigrationTallyConfiguration : IEntityTypeConfiguration<MigrationTally>
{
    public const string TableName = "migration_tally";

    public void Configure(EntityTypeBuilder<MigrationTally> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_migration_tally_population",
                $"population IN ({MigrationVocabulary.Counted()})");
            table.HasCheckConstraint(
                "ck_migration_tally_counts",
                """
                offered >= 0
                AND carried >= 0
                AND not_carried >= 0
                AND unclassified >= 0
                AND carried + not_carried + unclassified = offered
                """);
        });

        builder.HasKey(tally => new { tally.RunId, tally.Population });

        builder.Property(tally => tally.RunId)
            .HasConversion(id => id.Value, value => new MigrationRunId(value))
            .HasColumnName("run_id");

        builder.Property(tally => tally.Population)
            .HasConversion<string>()
            .HasMaxLength(MigrationVocabulary.NameLength)
            .HasColumnName("population");

        builder.Property(tally => tally.Offered).HasColumnName("offered").IsRequired();
        builder.Property(tally => tally.Carried).HasColumnName("carried").IsRequired();
        builder.Property(tally => tally.NotCarried).HasColumnName("not_carried").IsRequired();
        builder.Property(tally => tally.Unclassified).HasColumnName("unclassified").IsRequired();

        builder.HasOne<MigrationRun>()
            .WithMany()
            .HasForeignKey(tally => tally.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
