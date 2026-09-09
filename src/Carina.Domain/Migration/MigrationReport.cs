namespace Carina.Domain.Migration;

public sealed class MigrationReport
{
    private MigrationReport(
        MigrationRun run,
        IReadOnlyList<MigrationTally> tallies,
        IReadOnlyList<MigrationDetail> details,
        IReadOnlyList<MigrationLoss> losses,
        IReadOnlyList<MigrationChannelProposal> channelProposals,
        IReadOnlyList<MigrationRuleProposal> ruleProposals)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(tallies);
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(losses);
        ArgumentNullException.ThrowIfNull(channelProposals);
        ArgumentNullException.ThrowIfNull(ruleProposals);

        Dictionary<MigrationPopulation, MigrationTally> counted = Counted(run, tallies);
        Dictionary<MigrationPopulation, int> written = Written(run, details);

        foreach (MigrationPopulation population in MigrationPopulations.Counted)
        {
            if (!counted.TryGetValue(population, out MigrationTally? tally))
            {
                throw new MigrationUnclassifiedException(
                    $"The {population} population has no summary, so nothing about it can be said.");
            }

            if (tally.Unclassified is not 0)
            {
                throw new MigrationUnclassifiedException(
                    $"The {population} population left {tally.Unclassified} elements nobody could class.");
            }

            if (written.GetValueOrDefault(population) != tally.NotCarried)
            {
                throw new ArgumentException(
                    $"The {population} population left {tally.NotCarried} elements behind and "
                    + $"{written.GetValueOrDefault(population)} of them are written down.",
                    nameof(details));
            }
        }

        Lost(run, losses);
        Proposed(run, channelProposals, counted[MigrationPopulation.ChannelDefinitions]);
        Meant(run, ruleProposals, counted[MigrationPopulation.Rules]);

        Run = run;
        Tallies = [.. tallies];
        Details = [.. details];
        Losses = [.. losses];
        ChannelProposals = [.. channelProposals];
        RuleProposals = [.. ruleProposals];
    }

    public MigrationRun Run { get; }

    public IReadOnlyList<MigrationTally> Tallies { get; }

    public IReadOnlyList<MigrationDetail> Details { get; }

    public IReadOnlyList<MigrationLoss> Losses { get; }

    public IReadOnlyList<MigrationChannelProposal> ChannelProposals { get; }

    public IReadOnlyList<MigrationRuleProposal> RuleProposals { get; }

    public static MigrationReport Of(
        MigrationRun run,
        IReadOnlyList<MigrationTally> tallies,
        IReadOnlyList<MigrationDetail> details,
        IReadOnlyList<MigrationLoss> losses,
        IReadOnlyList<MigrationChannelProposal> channelProposals,
        IReadOnlyList<MigrationRuleProposal> ruleProposals)
        => new(run, tallies, details, losses, channelProposals, ruleProposals);

    private static Dictionary<MigrationPopulation, MigrationTally> Counted(
        MigrationRun run,
        IReadOnlyList<MigrationTally> tallies)
    {
        Dictionary<MigrationPopulation, MigrationTally> found = [];

        foreach (MigrationTally tally in tallies)
        {
            ArgumentNullException.ThrowIfNull(tally);
            Belongs(run, tally.RunId, nameof(tallies));

            if (!found.TryAdd(tally.Population, tally))
            {
                throw new ArgumentException(
                    $"A run counts the {tally.Population} population once.",
                    nameof(tallies));
            }
        }

        return found;
    }

    private static Dictionary<MigrationPopulation, int> Written(
        MigrationRun run,
        IReadOnlyList<MigrationDetail> details)
    {
        Dictionary<MigrationPopulation, int> found = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (MigrationDetail detail in details)
        {
            ArgumentNullException.ThrowIfNull(detail);
            Belongs(run, detail.RunId, nameof(details));

            if (!seen.Add($"{detail.Population}/{detail.Subject}"))
            {
                throw new ArgumentException(
                    $"A run says once why it left '{detail.Subject}' behind.",
                    nameof(details));
            }

            found[detail.Population] = found.GetValueOrDefault(detail.Population) + 1;
        }

        return found;
    }

    private static void Lost(MigrationRun run, IReadOnlyList<MigrationLoss> losses)
    {
        HashSet<MigrationLossSubject> found = [];

        foreach (MigrationLoss loss in losses)
        {
            ArgumentNullException.ThrowIfNull(loss);
            Belongs(run, loss.RunId, nameof(losses));

            if (!found.Add(loss.Subject))
            {
                throw new ArgumentException(
                    $"A run says once how much of {loss.Subject} it carried and left diminished.",
                    nameof(losses));
            }
        }

        foreach (MigrationLossSubject subject in MigrationLossSubjects.All)
        {
            if (!found.Contains(subject))
            {
                throw new ArgumentException(
                    $"What was carried of {subject} reached the new system diminished and the run does not say "
                    + "so, which later reads as a feature that went missing.",
                    nameof(losses));
            }
        }
    }

    private static void Proposed(
        MigrationRun run,
        IReadOnlyList<MigrationChannelProposal> proposals,
        MigrationTally definitions)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (MigrationChannelProposal proposal in proposals)
        {
            ArgumentNullException.ThrowIfNull(proposal);
            Belongs(run, proposal.RunId, nameof(proposals));

            if (!seen.Add($"{proposal.NetworkId.Value}/{proposal.ServiceId.Value}"))
            {
                throw new ArgumentException(
                    "A run says once what became of a service the source system defined.",
                    nameof(proposals));
            }
        }

        if (proposals.Count != definitions.Offered)
        {
            throw new ArgumentException(
                $"The source system defined {definitions.Offered} channels and the run says what became of "
                + $"{proposals.Count} of them.",
                nameof(proposals));
        }
    }

    private static void Meant(
        MigrationRun run,
        IReadOnlyList<MigrationRuleProposal> proposals,
        MigrationTally rules)
    {
        HashSet<long> seen = [];

        foreach (MigrationRuleProposal proposal in proposals)
        {
            ArgumentNullException.ThrowIfNull(proposal);
            Belongs(run, proposal.RunId, nameof(proposals));

            if (!seen.Add(proposal.SourceRow))
            {
                throw new ArgumentException(
                    "A run says once what the source system meant by a rule.",
                    nameof(proposals));
            }
        }

        if (proposals.Count != rules.Carried)
        {
            throw new ArgumentException(
                $"The run converted {rules.Carried} rules and says what the source system meant by "
                + $"{proposals.Count} of them.",
                nameof(proposals));
        }
    }

    private static void Belongs(MigrationRun run, MigrationRunId runId, string parameterName)
    {
        if (!runId.Equals(run.Id))
        {
            throw new ArgumentException("A report carries the rows of the run it is about and no others.", parameterName);
        }
    }
}
