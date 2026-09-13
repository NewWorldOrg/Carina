using Carina.Domain.Migration;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class MigrationStandingConfiguration : IEntityTypeConfiguration<MigrationStanding>
{
    public const string TableName = "migration_standing";

    public void Configure(EntityTypeBuilder<MigrationStanding> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_migration_standing_subject",
                $"subject IN ({MigrationVocabulary.Of<MigrationStandingSubject>()})");
            table.HasCheckConstraint(
                "ck_migration_standing_finding",
                string.Join(
                    "\nAND ",
                    MigrationStandingSubjects.All.Select(subject =>
                        $"(subject <> '{subject}' OR finding IN "
                        + $"({MigrationVocabulary.Naming(MigrationFindings.Under(subject))}))")));
        });

        builder.HasKey(standing => new { standing.RunId, standing.Subject });

        builder.Property(standing => standing.RunId)
            .HasConversion(id => id.Value, value => new MigrationRunId(value))
            .HasColumnName("run_id");

        builder.Property(standing => standing.Subject)
            .HasConversion<string>()
            .HasMaxLength(MigrationVocabulary.NameLength)
            .HasColumnName("subject");

        builder.Property(standing => standing.Finding)
            .HasConversion<string>()
            .HasMaxLength(MigrationVocabulary.NameLength)
            .HasColumnName("finding")
            .IsRequired();

        builder.HasOne<MigrationRun>()
            .WithMany()
            .HasForeignKey(standing => standing.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
