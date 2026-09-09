using Carina.Domain.Migration;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class MigrationLossConfiguration : IEntityTypeConfiguration<MigrationLoss>
{
    public const string TableName = "migration_loss";

    public void Configure(EntityTypeBuilder<MigrationLoss> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_migration_loss_subject",
                $"subject IN ({MigrationVocabulary.Of<MigrationLossSubject>()})");
            table.HasCheckConstraint("ck_migration_loss_affected", "affected >= 0");
        });

        builder.HasKey(loss => new { loss.RunId, loss.Subject });

        builder.Property(loss => loss.RunId)
            .HasConversion(id => id.Value, value => new MigrationRunId(value))
            .HasColumnName("run_id");

        builder.Property(loss => loss.Subject)
            .HasConversion<string>()
            .HasMaxLength(MigrationVocabulary.NameLength)
            .HasColumnName("subject");

        builder.Property(loss => loss.Affected)
            .HasColumnName("affected")
            .IsRequired();

        builder.HasOne<MigrationRun>()
            .WithMany()
            .HasForeignKey(loss => loss.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
