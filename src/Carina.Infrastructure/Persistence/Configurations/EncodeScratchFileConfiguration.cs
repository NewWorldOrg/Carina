using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class EncodeScratchFileConfiguration : IEntityTypeConfiguration<EncodeScratchFile>
{
    public const string NameIndexName = "ux_encode_scratch_file_name";

    public const string OwedIndexName = "ix_encode_scratch_file_owed";

    public void Configure(EntityTypeBuilder<EncodeScratchFile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("encode_scratch_file", table =>
        {
            table.HasCheckConstraint("ck_encode_scratch_file_kind", $"kind IN ({EncodeVocabulary.Of<EncodeScratchKind>()})");
            table.HasCheckConstraint("ck_encode_scratch_file_output_root", EncodeVocabulary.ASingleName("output_root"));
            table.HasCheckConstraint("ck_encode_scratch_file_name", EncodeVocabulary.ASingleName("file_name"));
            table.HasCheckConstraint(
                "ck_encode_scratch_file_removal",
                $"""
                ((removed_at IS NULL) = (fate IS NULL))
                AND (fate IS NULL OR fate IN ({EncodeVocabulary.Of<EncodeScratchFate>()}))
                AND (removed_at IS NULL OR removed_at >= written_at)
                """);
        });

        builder.HasKey(scratch => scratch.Id);

        builder.Property(scratch => scratch.Id)
            .HasConversion(id => id.Value, value => new EncodeScratchFileId(value))
            .HasColumnName("id");

        builder.Property(scratch => scratch.JobId)
            .HasConversion(id => id.Value, value => new EncodeJobId(value))
            .HasColumnName("job_id")
            .IsRequired();

        builder.Property(scratch => scratch.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(scratch => scratch.OutputRoot)
            .HasConversion(root => root.Value, value => new OutputRoot(value))
            .HasMaxLength(Carina.Domain.Recordings.OutputRoot.MaxLength)
            .IsRequired();

        builder.Property(scratch => scratch.FileName)
            .HasConversion(name => name.Value, value => new EncodeFileName(value))
            .HasMaxLength(EncodeFileName.MaxLength)
            .IsRequired();

        builder.Property(scratch => scratch.WrittenAt).IsRequired();
        builder.Property(scratch => scratch.RemovedAt);
        builder.Property(scratch => scratch.Fate).HasConversion<string>().HasMaxLength(32);

        builder.Ignore(scratch => scratch.IsOwedARemoval);

        builder.HasOne<EncodeJob>()
            .WithMany()
            .HasForeignKey(scratch => scratch.JobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(scratch => new { scratch.OutputRoot, scratch.FileName })
            .IsUnique()
            .HasDatabaseName(NameIndexName);

        builder.HasIndex(scratch => scratch.JobId)
            .HasFilter("removed_at IS NULL")
            .HasDatabaseName(OwedIndexName);
    }
}
