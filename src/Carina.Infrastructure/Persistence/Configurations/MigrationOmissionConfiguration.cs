using Carina.Domain.Migration;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class MigrationOmissionConfiguration : IEntityTypeConfiguration<MigrationOmission>
{
    public const string TableName = "migration_omission";

    public void Configure(EntityTypeBuilder<MigrationOmission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_migration_omission_subject",
                $"subject IN ({MigrationVocabulary.Of<MigrationOmissionSubject>()})");
            table.HasCheckConstraint(
                "ck_migration_omission_ground",
                MigrationVocabulary.EachThingLeftAlone("subject", "ground"));
            table.HasCheckConstraint(
                "ck_migration_omission_affected",
                MigrationVocabulary.EachCountKept("subject", "affected"));
            table.HasCheckConstraint(
                "ck_migration_omission_affected_counts",
                "affected IS NULL OR affected >= 0");
        });

        builder.HasKey(omission => new { omission.RunId, omission.Subject });

        builder.Property(omission => omission.RunId)
            .HasConversion(id => id.Value, value => new MigrationRunId(value))
            .HasColumnName("run_id");

        builder.Property(omission => omission.Subject)
            .HasConversion<string>()
            .HasMaxLength(MigrationVocabulary.NameLength)
            .HasColumnName("subject");

        builder.Property(omission => omission.Ground)
            .HasConversion<string>()
            .HasMaxLength(MigrationVocabulary.NameLength)
            .HasColumnName("ground")
            .IsRequired();

        builder.Property(omission => omission.Affected).HasColumnName("affected");

        builder.HasOne<MigrationRun>()
            .WithMany()
            .HasForeignKey(omission => omission.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
