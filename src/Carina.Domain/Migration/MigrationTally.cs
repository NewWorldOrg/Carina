namespace Carina.Domain.Migration;

public sealed class MigrationTally
{
    private MigrationTally()
    {
    }

    public MigrationRunId RunId { get; private set; } = null!;

    public MigrationPopulation Population { get; private set; }

    public int Offered { get; private set; }

    public int Carried { get; private set; }

    public int NotCarried { get; private set; }

    public int Unclassified { get; private set; }

    public static MigrationTally Rehydrate(
        MigrationRunId runId,
        MigrationPopulation population,
        int offered,
        int carried,
        int notCarried,
        int unclassified)
    {
        ArgumentNullException.ThrowIfNull(runId);

        int stood = Counted(offered, nameof(offered));
        int taken = Counted(carried, nameof(carried));
        int left = Counted(notCarried, nameof(notCarried));
        int loose = Counted(unclassified, nameof(unclassified));

        if (taken + left + loose != stood)
        {
            throw new ArgumentException(
                "What a population offered is what was carried, what was not, and what nobody could say.",
                nameof(offered));
        }

        return new MigrationTally
        {
            RunId = runId,
            Population = MigrationPopulations.Countable(population),
            Offered = stood,
            Carried = taken,
            NotCarried = left,
            Unclassified = loose,
        };
    }

    private static int Counted(int value, string parameterName)
        => value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, value, "A run counts nothing negative.");
}
