using Carina.Domain.Programmes;
using Carina.Domain.Rules;
using Carina.Infrastructure.Programmes;

namespace Carina.Infrastructure.Rules;

public sealed record RuleMatch(Rule Rule, ProgrammeMatch Programme);

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

    private static List<RuleMatch> Takes(
        Rule rule,
        ProgrammeSearch bound,
        IReadOnlyList<ProgrammeMatch> programmes,
        HashSet<ProgrammeKey> taken,
        DateTime at)
    {
        var takes = new List<RuleMatch>();

        foreach (ProgrammeMatch programme in programmes)
        {
            if (taken.Contains(Naming(programme))
                || !ProgrammeSearchMatching.Matches(programme, bound, at))
            {
                continue;
            }

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
