namespace Carina.Domain.Migration;

public sealed record MigrationCarry
{
    public MigrationCarry(MigrationCarryOutcome outcome, string said)
    {
        ArgumentNullException.ThrowIfNull(said);

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "A carry ends one of the ways the migration can name.");
        }

        Outcome = outcome;
        Said = MigrationNote.Of(said);
    }

    public MigrationCarryOutcome Outcome { get; }

    public string Said { get; }

    public bool Carried => Outcome is MigrationCarryOutcome.Linked;

    public static MigrationCarry Linked() => new(MigrationCarryOutcome.Linked, string.Empty);
}
