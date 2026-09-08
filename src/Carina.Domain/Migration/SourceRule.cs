namespace Carina.Domain.Migration;

public sealed record SourceRule
{
    public SourceRule(long id, string name, bool enabled, SourceRuleTerms terms, SourceRuleReach reach)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(reach);

        Id = SourceRow.Of(id, nameof(id));
        Name = name;
        Enabled = enabled;
        Terms = terms;
        Reach = reach;
    }

    public long Id { get; }

    public string Name { get; }

    public bool Enabled { get; }

    public SourceRuleTerms Terms { get; }

    public SourceRuleReach Reach { get; }

    public IReadOnlyList<ServiceKey> Services => Terms.Services;
}
