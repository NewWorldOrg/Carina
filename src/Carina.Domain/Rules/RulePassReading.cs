namespace Carina.Domain.Rules;

/// <summary>
/// What a pass of the rules read and took: the rules that failed to match, the rules still standing, the listings
/// they took, the listings the pass read, and whether it read the whole guide.
/// </summary>
public sealed record RulePassReading(
    IReadOnlySet<RuleId> Faulted,
    IReadOnlySet<RuleId> Standing,
    IReadOnlyList<ListingKey> Taken,
    IReadOnlySet<ListingKey> Seen,
    bool Sweeping);
