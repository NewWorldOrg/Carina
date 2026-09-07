namespace Carina.Domain.Migration;

public sealed class MigrationRoll
{
    private readonly Dictionary<MigrationPopulation, int> offered;

    private MigrationRoll(Dictionary<MigrationPopulation, int> offered, IReadOnlyList<MigrationVerdict> verdicts)
    {
        this.offered = offered;
        Verdicts = verdicts;
    }

    public IReadOnlyList<MigrationVerdict> Verdicts { get; }

    public IReadOnlyList<MigrationPopulation> Populations => [.. offered.Keys.Order()];

    public static MigrationRoll Of(
        IReadOnlyDictionary<MigrationPopulation, int> offered,
        IReadOnlyList<MigrationVerdict> verdicts)
    {
        ArgumentNullException.ThrowIfNull(offered);
        ArgumentNullException.ThrowIfNull(verdicts);

        Dictionary<MigrationPopulation, int> kept = [];

        foreach (KeyValuePair<MigrationPopulation, int> pair in offered)
        {
            if (pair.Value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offered),
                    pair.Value,
                    "A population offers nothing fewer than none of its elements.");
            }

            kept[MigrationPopulations.Named(pair.Key)] = pair.Value;
        }

        foreach (MigrationVerdict verdict in verdicts)
        {
            ArgumentNullException.ThrowIfNull(verdict);
        }

        return new MigrationRoll(kept, [.. verdicts]);
    }

    public int OfferedIn(MigrationPopulation population)
        => offered.TryGetValue(MigrationPopulations.Named(population), out int count)
            ? count
            : throw new MigrationUnclassifiedException(
                $"The {population} population was never read, so nothing about it can be said.");
}
