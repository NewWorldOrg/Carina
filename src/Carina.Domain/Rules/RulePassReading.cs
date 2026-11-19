namespace Carina.Domain.Rules;

public sealed record RulePassReading(
    IReadOnlySet<RuleId> Faulted,
    IReadOnlySet<RuleId> Standing,
    IReadOnlyList<ListingKey> Taken,
    IReadOnlySet<ListingKey> Seen,
    bool Sweeping);
