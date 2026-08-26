using Carina.Domain.Integrity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class IntegrityCheckConfiguration : IEntityTypeConfiguration<IntegrityCheck>
{
    public const string FinishedIndexName = "ix_integrity_check_finished";

    public void Configure(EntityTypeBuilder<IntegrityCheck> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("integrity_check", table =>
        {
            table.HasCheckConstraint("ck_integrity_check_span", "finished_at >= started_at");
            table.HasCheckConstraint(
                "ck_integrity_check_counts",
                """
                roots_walked >= 0
                AND roots_out_of_reach >= 0
                AND files_read >= 0
                AND ledger_rows_read >= 0
                AND ledger_rows_judged >= 0
                AND ledger_rows_still_writing >= 0
                AND ledger_rows_in_roots_out_of_reach >= 0
                """);
        });

        builder.HasKey(check => check.Id);

        builder.Property(check => check.Id)
            .HasConversion(id => id.Value, value => new IntegrityCheckId(value))
            .HasColumnName("id");

        builder.Property(check => check.StartedAt).IsRequired();
        builder.Property(check => check.FinishedAt).IsRequired();
        builder.Property(check => check.RootsWalked).IsRequired();
        builder.Property(check => check.RootsOutOfReach).IsRequired();
        builder.Property(check => check.FilesRead).IsRequired();
        builder.Property(check => check.LedgerRowsRead).IsRequired();
        builder.Property(check => check.LedgerRowsJudged).IsRequired();
        builder.Property(check => check.LedgerRowsStillWriting).IsRequired();
        builder.Property(check => check.LedgerRowsInRootsOutOfReach).IsRequired();

        builder.HasIndex(check => check.FinishedAt).HasDatabaseName(FinishedIndexName);
    }
}
