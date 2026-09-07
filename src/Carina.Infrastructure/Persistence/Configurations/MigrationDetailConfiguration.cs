using Carina.Domain.Migration;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class MigrationDetailConfiguration : IEntityTypeConfiguration<MigrationDetail>
{
    public const string TableName = "migration_detail";

    public const string RunIndexName = "ix_migration_detail_run";

    public const string SubjectIndexName = "ux_migration_detail_subject";

    public void Configure(EntityTypeBuilder<MigrationDetail> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_migration_detail_population",
                $"population IN ({MigrationVocabulary.Counted()})");
            table.HasCheckConstraint(
                "ck_migration_detail_refusal",
                $"refusal IN ({MigrationVocabulary.Of<MigrationRefusal>()})");
            table.HasCheckConstraint("ck_migration_detail_subject", "length(subject) > 0");
            table.HasCheckConstraint(
                "ck_migration_detail_sizes",
                "(claimed IS NULL OR claimed >= 0) AND (observed IS NULL OR observed >= 0)");
        });

        builder.HasKey(detail => detail.Id);

        builder.Property(detail => detail.Id)
            .HasConversion(id => id.Value, value => new MigrationDetailId(value))
            .HasColumnName("id");

        builder.Property(detail => detail.RunId)
            .HasConversion(id => id.Value, value => new MigrationRunId(value))
            .HasColumnName("run_id")
            .IsRequired();

        builder.Property(detail => detail.Population)
            .HasConversion<string>()
            .HasMaxLength(MigrationVocabulary.NameLength)
            .HasColumnName("population")
            .IsRequired();

        builder.Property(detail => detail.Refusal)
            .HasConversion<string>()
            .HasMaxLength(MigrationVocabulary.NameLength)
            .HasColumnName("refusal")
            .IsRequired();

        builder.Property(detail => detail.Subject)
            .HasMaxLength(MigrationDetail.SubjectMaxLength)
            .HasColumnName("subject")
            .IsRequired();

        builder.Property(detail => detail.Note)
            .HasMaxLength(MigrationNote.Longest)
            .HasColumnName("note")
            .IsRequired();

        builder.Property(detail => detail.Claimed).HasColumnName("claimed");
        builder.Property(detail => detail.Observed).HasColumnName("observed");

        builder.HasOne<MigrationRun>()
            .WithMany()
            .HasForeignKey(detail => detail.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(detail => new { detail.RunId, detail.Refusal }).HasDatabaseName(RunIndexName);

        builder.HasIndex(detail => new { detail.RunId, detail.Population, detail.Subject })
            .IsUnique()
            .HasDatabaseName(SubjectIndexName);
    }
}
