using Carina.Domain.Scans;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class ScanRunConfiguration : IEntityTypeConfiguration<ScanRun>
{
    public const string RunningIndexName = "ux_scan_run_running";

    public void Configure(EntityTypeBuilder<ScanRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("scan_run", table =>
        {
            table.HasCheckConstraint(
                "ck_scan_run_state",
                "state IN ('Running', 'Completed', 'Failed', 'Cancelled', 'Interrupted')");
            table.HasCheckConstraint(
                "ck_scan_run_finished",
                "(state = 'Running') = (finished_at IS NULL)");
            table.HasCheckConstraint(
                "ck_scan_run_reason",
                "state NOT IN ('Failed', 'Cancelled') OR (reason IS NOT NULL AND length(btrim(reason)) > 0)");
        });

        builder.HasKey(run => run.Id);

        builder.Property(run => run.Id)
            .HasConversion(id => id.Value, value => new ScanRunId(value))
            .HasColumnName("id");

        builder.Property(run => run.State)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(run => run.DriverInstanceId).HasMaxLength(64);

        builder.Property(run => run.Reason).HasMaxLength(ScanRun.ReasonMaxLength);

        builder.Property(run => run.StartedAt).IsRequired();

        builder.HasIndex(run => run.State)
            .IsUnique()
            .HasFilter("state = 'Running'")
            .HasDatabaseName(RunningIndexName);

        builder.HasIndex(run => run.StartedAt);
    }
}
