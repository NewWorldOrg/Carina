namespace Carina.Domain.Migration;

public sealed record MigrationVerdict
{
    private MigrationVerdict(
        MigrationPopulation population,
        string subject,
        MigrationRefusal? refusal,
        string note,
        long? claimed,
        long? observed)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        ArgumentNullException.ThrowIfNull(note);

        Population = MigrationPopulations.Named(population);
        Subject = subject;
        Refusal = refusal is { } named ? MigrationRefusals.Named(named) : null;
        Note = MigrationNote.Of(note);
        Claimed = Weighed(claimed, nameof(claimed));
        Observed = Weighed(observed, nameof(observed));
    }

    public MigrationPopulation Population { get; }

    public string Subject { get; }

    public MigrationRefusal? Refusal { get; }

    public string Note { get; }

    public long? Claimed { get; }

    public long? Observed { get; }

    public bool Carried => Refusal is null;

    public static MigrationVerdict Carry(
        MigrationPopulation population,
        string subject,
        string note,
        long? claimed,
        long? observed)
        => new(population, subject, null, note, claimed, observed);

    public static MigrationVerdict Refuse(
        MigrationPopulation population,
        string subject,
        MigrationRefusal refusal,
        string note,
        long? claimed,
        long? observed)
        => new(population, subject, refusal, note, claimed, observed);

    private static long? Weighed(long? size, string parameterName)
        => size is null or >= 0
            ? size
            : throw new ArgumentOutOfRangeException(parameterName, size, "A file is not smaller than empty.");
}
