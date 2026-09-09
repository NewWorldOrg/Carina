namespace Carina.Domain.Migration;

public sealed record MigrationRefusalCount(MigrationRefusal Refusal, int Count)
{
    public static IReadOnlyList<MigrationRefusalCount> EveryOne(IReadOnlyDictionary<MigrationRefusal, int> counted)
    {
        ArgumentNullException.ThrowIfNull(counted);

        foreach (MigrationRefusal refusal in counted.Keys)
        {
            MigrationRefusals.Named(refusal);
        }

        return
        [
            .. MigrationRefusals.All.Select(refusal =>
                new MigrationRefusalCount(refusal, counted.GetValueOrDefault(refusal))),
        ];
    }
}

public sealed record MigrationRecordSummary(
    MigrationRun Run,
    IReadOnlyList<MigrationTally> Tallies,
    IReadOnlyList<MigrationRefusalCount> Refusals,
    IReadOnlyList<MigrationLoss> Losses,
    int Rehearsals,
    DateTime? LastRehearsalFinishedAt)
{
    public int Unclassified => Tallies.Sum(tally => tally.Unclassified);
}
