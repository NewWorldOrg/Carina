using Carina.Domain.Encodings;

namespace Carina.Domain.Migration;

public static class MigrationCensus
{
    public static MigrationReport Taken(
        MigrationRunId id,
        MigrationSourceName source,
        MigrationPass pass,
        MigrationRoll roll,
        MigrationAftermath aftermath,
        MigrationRootStanding newRoot,
        EncodeUnaskedStanding whereEncodesGo,
        DateTime startedAt,
        DateTime finishedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(roll);
        ArgumentNullException.ThrowIfNull(aftermath);

        foreach (MigrationPopulation population in roll.Populations)
        {
            MigrationPopulations.Countable(population);
        }

        Dictionary<MigrationPopulation, int> carried = [];
        Dictionary<MigrationPopulation, int> refused = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<MigrationDetail> details = [];

        foreach (MigrationVerdict verdict in roll.Verdicts)
        {
            MigrationPopulation population = MigrationPopulations.Countable(verdict.Population);

            if (!seen.Add($"{population}/{verdict.Subject}"))
            {
                throw new MigrationUnclassifiedException(
                    $"'{verdict.Subject}' of the {population} population was judged twice, which leaves "
                    + "another element with no judgement at all.");
            }

            if (verdict.Carried)
            {
                carried[population] = carried.GetValueOrDefault(population) + 1;
                continue;
            }

            refused[population] = refused.GetValueOrDefault(population) + 1;
            details.Add(MigrationDetail.Of(id, verdict));
        }

        List<MigrationTally> tallies = [];

        foreach (MigrationPopulation population in MigrationPopulations.Counted)
        {
            int offered = roll.OfferedIn(population);
            int taken = carried.GetValueOrDefault(population);
            int left = refused.GetValueOrDefault(population);
            int loose = offered - taken - left;

            if (loose < 0)
            {
                throw new MigrationUnclassifiedException(
                    $"The {population} population offered {offered} elements and {taken + left} judgements "
                    + "were made about it.");
            }

            if (loose is not 0)
            {
                throw new MigrationUnclassifiedException(
                    $"The {population} population left {loose} of its {offered} elements with no judgement, "
                    + "and an element nobody can explain is what this run exists to rule out.");
            }

            tallies.Add(MigrationTally.Rehydrate(id, population, offered, taken, left, loose));
        }

        MigrationRun run = MigrationRun.Rehydrate(id, source, pass, startedAt, finishedAt);

        return MigrationReport.Of(
            run,
            tallies,
            details,
            MigrationLoss.EveryOne(id, aftermath),
            MigrationStanding.EveryOne(id, newRoot, whereEncodesGo),
            aftermath.ChannelProposals,
            aftermath.RuleProposals);
    }
}
