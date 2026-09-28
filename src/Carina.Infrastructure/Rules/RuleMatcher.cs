using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Programmes;

namespace Carina.Infrastructure.Rules;

public sealed record RuleMatch(Rule Rule, ProgrammeMatch Programme)
{
    public BroadcastGroupKey? GroupKey { get; init; }

    public BroadcastGroupRole GroupRole { get; init; } = BroadcastGroupRole.Standalone;
}

/// <summary>
/// The programmes the rules take once each match is replaced by the programmes that stand for its
/// broadcast, and how many matches were listings a moved broadcast suppresses.
/// </summary>
public sealed record RuleTaking(IReadOnlyList<RuleMatch> Matches, int Moved);

public sealed record RuleFault(Rule Rule, Exception Cause);

public sealed record RuleMatchRun(
    IReadOnlyList<RuleMatch> Matches,
    IReadOnlyList<Rule> TurnedOff,
    IReadOnlyList<RuleFault> Faulted);

public sealed class RuleMatcher(ProgrammeSearchScope scope, TimeProvider clock)
{
    public static IReadOnlyList<Rule> InPrecedence(IEnumerable<Rule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return
        [
            .. rules
                .OrderByDescending(rule => rule.Priority.Value)
                .ThenBy(rule => rule.CreatedAt)
                .ThenBy(rule => rule.Id.Value.ToString(), StringComparer.Ordinal),
        ];
    }

    public async Task<RuleMatchRun> AgainstAsync(
        IReadOnlyList<Rule> rules,
        IReadOnlyList<ProgrammeMatch> programmes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(programmes);

        DateTime at = clock.GetUtcNow().UtcDateTime;
        ProgrammeSearchBounds bounds = await scope.ReadAsync(cancellationToken);
        var taken = new HashSet<ProgrammeKey>();
        var found = new List<RuleMatch>();
        var turnedOff = new List<Rule>();
        var faulted = new List<RuleFault>();

        foreach (Rule rule in InPrecedence(rules))
        {
            if (!rule.Enabled)
            {
                continue;
            }

            if (ProgrammeSearchQuery.Read(rule.Query.Value) is not { } asked)
            {
                rule.Disable();
                turnedOff.Add(rule);

                continue;
            }

            List<RuleMatch> takes;

            try
            {
                takes = Takes(rule, bounds.Bound(asked), programmes, taken, at);
            }
            catch (Exception cause) when (cause is not OperationCanceledException)
            {
                faulted.Add(new RuleFault(rule, cause));

                continue;
            }

            foreach (RuleMatch take in takes)
            {
                taken.Add(Naming(take.Programme));
                found.Add(take);
            }
        }

        return new RuleMatchRun(found, turnedOff, faulted);
    }

    /// <summary>
    /// Replaces every match by the programmes that stand for its broadcast: the primary listing of a
    /// moved broadcast, every segment still to come of a relayed one. The first rule to reach a
    /// programme keeps it, as it does before the replacement.
    /// </summary>
    public static RuleTaking Resolved(
        IReadOnlyList<RuleMatch> matches,
        BroadcastGroupResolver groups,
        IReadOnlyList<Programme> read,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(read);

        Dictionary<ProgrammeKey, Programme> held = [];

        foreach (Programme programme in read)
        {
            held.TryAdd(Naming(ProgrammeMatch.Of(programme)), programme);
        }

        List<RuleMatch> taking = [];
        HashSet<ProgrammeKey> claimed = [];
        int moved = 0;

        foreach (RuleMatch match in matches)
        {
            if (!held.TryGetValue(Naming(match.Programme), out Programme? programme))
            {
                if (claimed.Add(Naming(match.Programme)))
                {
                    taking.Add(match);
                }

                continue;
            }

            BroadcastResolution resolution = groups.Resolve(programme, at);

            if (resolution.Exclusion is BroadcastExclusion.Moved)
            {
                moved++;
            }

            foreach (BroadcastTarget target in resolution.Targets)
            {
                ProgrammeMatch standing = ReferenceEquals(target.Programme, programme)
                    ? match.Programme
                    : ProgrammeMatch.Of(target.Programme);

                if (claimed.Add(Naming(standing)))
                {
                    taking.Add(new RuleMatch(match.Rule, standing) { GroupKey = target.Key, GroupRole = target.Role });
                }
            }
        }

        return new RuleTaking(taking, moved);
    }

    public async Task<int> ShadowedByAsync(
        Rule rule,
        IReadOnlyList<ProgrammeMatch> programmes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(programmes);

        if (ProgrammeSearchQuery.Read(rule.Query.Value) is not { } asked)
        {
            return 0;
        }

        DateTime at = clock.GetUtcNow().UtcDateTime;
        ProgrammeSearchBounds bounds = await scope.ReadAsync(cancellationToken);
        ProgrammeSearch bound = bounds.Bound(asked);

        return programmes.Count(programme =>
            programme.IsShadow && ProgrammeSearchMatching.MatchesBesideBeingAShadow(programme, bound, at));
    }

    private static List<RuleMatch> Takes(
        Rule rule,
        ProgrammeSearch bound,
        IReadOnlyList<ProgrammeMatch> programmes,
        HashSet<ProgrammeKey> taken,
        DateTime at)
    {
        var takes = new List<RuleMatch>();
        var claiming = new HashSet<ProgrammeKey>(taken);

        foreach (ProgrammeMatch programme in programmes)
        {
            ProgrammeKey naming = Naming(programme);

            if (claiming.Contains(naming) || !ProgrammeSearchMatching.Matches(programme, bound, at))
            {
                continue;
            }

            claiming.Add(naming);
            takes.Add(new RuleMatch(rule, programme));
        }

        return takes;
    }

    private static ProgrammeKey Naming(ProgrammeMatch programme)
        => new(
            programme.NetworkId.Value,
            programme.ServiceId.Value,
            programme.EventId.Value,
            programme.StartsAt);

    private readonly record struct ProgrammeKey(int NetworkId, int ServiceId, int EventId, DateTime StartsAt);
}
