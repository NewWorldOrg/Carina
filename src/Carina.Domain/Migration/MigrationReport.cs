namespace Carina.Domain.Migration;

public sealed class MigrationReport
{
    private MigrationReport(
        MigrationRun run,
        IReadOnlyList<MigrationTally> tallies,
        IReadOnlyList<MigrationDetail> details,
        IReadOnlyList<MigrationOmission> omissions)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(tallies);
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(omissions);

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

        Told(run, omissions);

        Run = run;
        Tallies = [.. tallies];
        Details = [.. details];
        Omissions = [.. omissions];
    }

    public MigrationRun Run { get; }

    public IReadOnlyList<MigrationTally> Tallies { get; }

    public IReadOnlyList<MigrationDetail> Details { get; }

    public IReadOnlyList<MigrationOmission> Omissions { get; }

    public static MigrationReport Of(
        MigrationRun run,
        IReadOnlyList<MigrationTally> tallies,
        IReadOnlyList<MigrationDetail> details,
        IReadOnlyList<MigrationOmission> omissions)
        => new(run, tallies, details, omissions);

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

    private static void Told(MigrationRun run, IReadOnlyList<MigrationOmission> omissions)
    {
        HashSet<MigrationOmissionSubject> found = [];

        foreach (MigrationOmission omission in omissions)
        {
            ArgumentNullException.ThrowIfNull(omission);
            Belongs(run, omission.RunId, nameof(omissions));

            if (!found.Add(omission.Subject))
            {
                throw new ArgumentException(
                    $"A run says once that it did nothing about {omission.Subject}.",
                    nameof(omissions));
            }
        }

        foreach (MigrationOmissionSubject subject in MigrationOmissionSubjects.All)
        {
            if (!found.Contains(subject))
            {
                throw new ArgumentException(
                    $"Nothing was done about {subject} and the run does not say so, which later reads as a "
                    + "feature that went missing.",
                    nameof(omissions));
            }
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
