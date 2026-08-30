namespace Carina.Api.Requests;

public sealed record SaveRuleRequest
{
    public string? Name { get; init; }

    public string? Query { get; init; }

    public int? Priority { get; init; }

    public bool? Enabled { get; init; }

    public int? MarginBeforeSeconds { get; init; }

    public int? MarginAfterSeconds { get; init; }
}

public sealed record RuleEnabledRequest
{
    public bool? Enabled { get; init; }
}

public sealed record RuleDraftRequest
{
    public Guid? RuleId { get; init; }

    public string? Query { get; init; }

    public int? Priority { get; init; }

    public int? MarginBeforeSeconds { get; init; }

    public int? MarginAfterSeconds { get; init; }
}
