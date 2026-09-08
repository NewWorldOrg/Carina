using Carina.Domain.Migration;
using Carina.Domain.Rules;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class MigrationRuleProposalConfiguration : IEntityTypeConfiguration<MigrationRuleProposal>
{
    public const string TableName = "migration_rule_proposal";

    public void Configure(EntityTypeBuilder<MigrationRuleProposal> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
            table.HasCheckConstraint("ck_migration_rule_proposal_source_row", "source_row > 0"));

        builder.HasKey(proposal => new { proposal.RunId, proposal.SourceRow });

        builder.Property(proposal => proposal.RunId)
            .HasConversion(id => id.Value, value => new MigrationRunId(value))
            .HasColumnName("run_id");

        builder.Property(proposal => proposal.SourceRow).HasColumnName("source_row");

        builder.Property(proposal => proposal.RuleId)
            .HasConversion(id => id!.Value, value => new RuleId(value))
            .HasColumnName("rule_id");

        builder.Property(proposal => proposal.EnabledAtTheSource)
            .HasColumnName("enabled_at_the_source")
            .IsRequired();

        builder.HasOne<MigrationRun>()
            .WithMany()
            .HasForeignKey(proposal => proposal.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
