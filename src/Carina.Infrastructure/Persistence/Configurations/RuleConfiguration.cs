using Carina.Domain.Reservations;
using Carina.Domain.Rules;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class RuleConfiguration : IEntityTypeConfiguration<Rule>
{
    public const string PrecedenceIndexName = "ix_rule_precedence";

    public void Configure(EntityTypeBuilder<Rule> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("rule", table =>
        {
            table.HasCheckConstraint(
                "ck_rule_priority",
                $"priority BETWEEN {Priority.MinValue} AND {Priority.MaxValue}");
            table.HasCheckConstraint(
                "ck_rule_margins",
                $"margin_before BETWEEN 0 AND {(int)Margin.Longest.TotalSeconds} "
                + $"AND margin_after BETWEEN 0 AND {(int)Margin.Longest.TotalSeconds}");
            table.HasCheckConstraint("ck_rule_query", "length(btrim(query)) > 0");
        });

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Id)
            .HasConversion(id => id.Value, value => new RuleId(value))
            .HasColumnName("id");

        builder.Property(rule => rule.Name)
            .HasMaxLength(Rule.NameMaxLength)
            .IsRequired();

        builder.Property(rule => rule.Query)
            .HasConversion(query => query.Value, value => new RuleQuery(value))
            .HasMaxLength(RuleQuery.MaxLength)
            .IsRequired();

        builder.Property(rule => rule.Priority)
            .HasConversion(priority => priority.Value, value => new Priority(value))
            .IsRequired();

        builder.Property(rule => rule.Enabled).IsRequired();

        builder.Property(rule => rule.MarginBefore)
            .HasConversion(margin => margin.Seconds, value => Margin.OfSeconds(value))
            .HasColumnName("margin_before")
            .IsRequired();

        builder.Property(rule => rule.MarginAfter)
            .HasConversion(margin => margin.Seconds, value => Margin.OfSeconds(value))
            .HasColumnName("margin_after")
            .IsRequired();

        builder.Property(rule => rule.CreatedAt).IsRequired();

        builder.HasIndex(rule => new { rule.Priority, rule.CreatedAt, rule.Id })
            .IsDescending(true, false, false)
            .HasFilter("enabled")
            .HasDatabaseName(PrecedenceIndexName);
    }
}
