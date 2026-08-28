using Carina.Domain.Programmes;
using Carina.Domain.Rules;
using Carina.Infrastructure.Programmes;

namespace Carina.Infrastructure.Rules;

public sealed record RuleMatch(Rule Rule, ProgrammeMatch Programme);

public sealed record RuleMatchRun(IReadOnlyList<RuleMatch> Matches, IReadOnlyList<Rule> TurnedOff);

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
        var taken = new HashSet<ProgrammeKey>();
        var found = new List<RuleMatch>();
        var turnedOff = new List<Rule>();

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

            ProgrammeSearch bound = await scope.BoundAsync(asked, cancellationToken);

            foreach (ProgrammeMatch programme in programmes)
            {
                if (taken.Contains(Naming(programme))
                    || !ProgrammeSearchMatching.Matches(programme, bound, at))
                {
                    continue;
                }

                taken.Add(Naming(programme));
                found.Add(new RuleMatch(rule, programme));
            }
        }

        return new RuleMatchRun(found, turnedOff);
    }

    private static ProgrammeKey Naming(ProgrammeMatch programme)
        => new(
            programme.NetworkId.Value,
            programme.ServiceId.Value,
            programme.EventId.Value,
            programme.StartsAt);

    private readonly record struct ProgrammeKey(int NetworkId, int ServiceId, int EventId, DateTime StartsAt);
}
