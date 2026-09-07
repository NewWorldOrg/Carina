namespace Carina.Domain.Migration;

public sealed class MigrationDetail
{
    public const int SubjectMaxLength = 512;

    private MigrationDetail()
    {
    }

    public MigrationDetailId Id { get; private set; } = null!;

    public MigrationRunId RunId { get; private set; } = null!;

    public MigrationPopulation Population { get; private set; }

    public MigrationRefusal Refusal { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public string Note { get; private set; } = string.Empty;

    public long? Claimed { get; private set; }

    public long? Observed { get; private set; }

    public static MigrationDetail Rehydrate(
        MigrationDetailId id,
        MigrationRunId runId,
        MigrationPopulation population,
        MigrationRefusal refusal,
        string subject,
        string note,
        long? claimed,
        long? observed)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(runId);
        ArgumentException.ThrowIfNullOrEmpty(subject);
        ArgumentNullException.ThrowIfNull(note);

        if (subject.Length > SubjectMaxLength)
        {
            throw new ArgumentException(
                $"A subject is at most {SubjectMaxLength} characters, but this one has {subject.Length}.",
                nameof(subject));
        }

        return new MigrationDetail
        {
            Id = id,
            RunId = runId,
            Population = MigrationPopulations.Countable(population),
            Refusal = MigrationRefusals.Named(refusal),
            Subject = subject,
            Note = MigrationNote.Of(note),
            Claimed = Weighed(claimed, nameof(claimed)),
            Observed = Weighed(observed, nameof(observed)),
        };
    }

    public static MigrationDetail Of(MigrationRunId runId, MigrationVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        if (verdict.Refusal is not { } refusal)
        {
            throw new ArgumentException(
                "The detail of a run is what it did not carry, so a verdict that carried has no line here.",
                nameof(verdict));
        }

        return Rehydrate(
            MigrationDetailId.New(),
            runId,
            verdict.Population,
            refusal,
            verdict.Subject,
            verdict.Note,
            verdict.Claimed,
            verdict.Observed);
    }

    private static long? Weighed(long? size, string parameterName)
        => size is null or >= 0
            ? size
            : throw new ArgumentOutOfRangeException(parameterName, size, "A file is not smaller than empty.");
}
