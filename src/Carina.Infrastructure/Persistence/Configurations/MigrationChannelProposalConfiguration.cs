using Carina.Domain.Channels;
using Carina.Domain.Migration;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class MigrationChannelProposalConfiguration : IEntityTypeConfiguration<MigrationChannelProposal>
{
    public const string TableName = "migration_channel_proposal";

    public void Configure(EntityTypeBuilder<MigrationChannelProposal> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_migration_channel_proposal_standing",
                $"standing IN ({MigrationVocabulary.Of<MigrationChannelStanding>()})");
            table.HasCheckConstraint(
                "ck_migration_channel_proposal_name",
                $"(standing = '{MigrationChannelStanding.NameProposed}') = (rescanned_name IS NOT NULL)");
        });

        builder.HasKey(proposal => new { proposal.RunId, proposal.NetworkId, proposal.ServiceId });

        builder.Property(proposal => proposal.RunId)
            .HasConversion(id => id.Value, value => new MigrationRunId(value))
            .HasColumnName("run_id");

        builder.Property(proposal => proposal.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id");

        builder.Property(proposal => proposal.ServiceId)
            .HasConversion(id => id.Value, value => new ServiceId(value))
            .HasColumnName("service_id");

        builder.Property(proposal => proposal.Standing)
            .HasConversion<string>()
            .HasMaxLength(MigrationVocabulary.NameLength)
            .HasColumnName("standing")
            .IsRequired();

        builder.Property(proposal => proposal.SourceName)
            .HasMaxLength(MigrationChannelProposal.NameMaxLength)
            .HasColumnName("source_name")
            .IsRequired();

        builder.Property(proposal => proposal.SourcePhysicalChannel)
            .HasMaxLength(SourceChannelDefinition.PhysicalChannelMaxLength)
            .HasColumnName("source_physical_channel")
            .IsRequired();

        builder.Property(proposal => proposal.RescannedName)
            .HasMaxLength(MigrationChannelProposal.NameMaxLength)
            .HasColumnName("rescanned_name");

        builder.HasOne<MigrationRun>()
            .WithMany()
            .HasForeignKey(proposal => proposal.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
