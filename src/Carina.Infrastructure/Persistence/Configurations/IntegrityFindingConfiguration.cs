using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class IntegrityFindingConfiguration : IEntityTypeConfiguration<IntegrityFinding>
{
    public const string CheckIndexName = "ix_integrity_finding_check";

    public const string RecordingIndexName = "ix_integrity_finding_recording";

    private static string Vocabulary(IEnumerable<IntegrityFault> faults)
        => string.Join(", ", faults.Select(fault => $"'{fault}'"));

    public void Configure(EntityTypeBuilder<IntegrityFinding> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("integrity_finding", table =>
        {
            string named = Vocabulary(IntegrityFaults.ThatNameARecording);
            string weighed = Vocabulary(IntegrityFaults.ThatWeighedTheFile);

            table.HasCheckConstraint(
                "ck_integrity_finding_fault",
                $"fault IN ({Vocabulary(Enum.GetValues<IntegrityFault>())})");
            table.HasCheckConstraint(
                "ck_integrity_finding_recording",
                $"(fault IN ({named})) = (recording_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_integrity_finding_ledger_size",
                $"(fault IN ({named})) = (ledger_size IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_integrity_finding_observed_size",
                $"(fault IN ({weighed})) = (observed_size IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_integrity_finding_sizes",
                "(ledger_size IS NULL OR ledger_size >= 0) AND (observed_size IS NULL OR observed_size >= 0)");
            table.HasCheckConstraint(
                "ck_integrity_finding_path",
                "length(path) > 0 AND left(path, 1) <> '/'");
        });

        builder.HasKey(finding => finding.Id);

        builder.Property(finding => finding.Id)
            .HasConversion(id => id.Value, value => new IntegrityFindingId(value))
            .HasColumnName("id");

        builder.Property(finding => finding.CheckId)
            .HasConversion(id => id.Value, value => new IntegrityCheckId(value))
            .HasColumnName("check_id")
            .IsRequired();

        builder.Property(finding => finding.Fault)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasColumnName("fault")
            .IsRequired();

        builder.Property(finding => finding.Root)
            .HasConversion(root => root.Value, value => new OutputRoot(value))
            .HasMaxLength(OutputRoot.MaxLength)
            .HasColumnName("output_root")
            .IsRequired();

        builder.Property(finding => finding.Path)
            .HasColumnType("text")
            .HasColumnName("path")
            .IsRequired();

        builder.Property(finding => finding.RecordingId)
            .HasConversion(id => id!.Value, value => new RecordingId(value))
            .HasColumnName("recording_id");

        builder.Property(finding => finding.LedgerSize).HasColumnName("ledger_size");
        builder.Property(finding => finding.ObservedSize).HasColumnName("observed_size");
        builder.Property(finding => finding.NoticedAt).IsRequired();

        builder.HasOne<IntegrityCheck>()
            .WithMany()
            .HasForeignKey(finding => finding.CheckId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(finding => new { finding.CheckId, finding.Fault }).HasDatabaseName(CheckIndexName);

        builder.HasIndex(finding => finding.RecordingId)
            .HasFilter("recording_id IS NOT NULL")
            .HasDatabaseName(RecordingIndexName);
    }
}
