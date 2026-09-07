using Carina.Domain.Migration;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class MigrationRunConfiguration : IEntityTypeConfiguration<MigrationRun>
{
    public const string TableName = "migration_run";

    public const string FinishedIndexName = "ix_migration_run_finished";

    public void Configure(EntityTypeBuilder<MigrationRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint("ck_migration_run_span", "finished_at >= started_at");
            table.HasCheckConstraint("ck_migration_run_pass", $"pass IN ({MigrationVocabulary.Of<MigrationPass>()})");
            table.HasCheckConstraint("ck_migration_run_source", "length(source) > 0");
        });

        builder.HasKey(run => run.Id);

        builder.Property(run => run.Id)
            .HasConversion(id => id.Value, value => new MigrationRunId(value))
            .HasColumnName("id");

        builder.Property(run => run.Source)
            .HasConversion(source => source.Value, value => new MigrationSourceName(value))
            .HasMaxLength(MigrationSourceName.MaxLength)
            .HasColumnName("source")
            .IsRequired();

        builder.Property(run => run.Pass)
            .HasConversion<string>()
            .HasMaxLength(MigrationVocabulary.NameLength)
            .HasColumnName("pass")
            .IsRequired();

        builder.Property(run => run.StartedAt).IsRequired();
        builder.Property(run => run.FinishedAt).IsRequired();

        builder.HasIndex(run => run.FinishedAt).HasDatabaseName(FinishedIndexName);
    }
}
