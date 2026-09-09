namespace Carina.Domain.Migration;

public sealed class MigrationLoss
{
    private MigrationLoss()
    {
    }

    public MigrationRunId RunId { get; private set; } = null!;

    public MigrationLossSubject Subject { get; private set; }

    public int Affected { get; private set; }

    public static MigrationLoss Rehydrate(MigrationRunId runId, MigrationLossSubject subject, int affected)
    {
        ArgumentNullException.ThrowIfNull(runId);

        if (affected < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(affected), affected, "A run counts nothing negative.");
        }

        return new MigrationLoss
        {
            RunId = runId,
            Subject = MigrationLossSubjects.Named(subject),
            Affected = affected,
        };
    }

    public static IReadOnlyList<MigrationLoss> EveryOne(MigrationRunId runId, MigrationAftermath aftermath)
    {
        ArgumentNullException.ThrowIfNull(aftermath);

        return
        [
            .. MigrationLossSubjects.All.Select(subject => Rehydrate(runId, subject, Reaching(subject, aftermath))),
        ];
    }

    private static int Reaching(MigrationLossSubject subject, MigrationAftermath aftermath)
        => MigrationLossSubjects.Named(subject) switch
        {
            MigrationLossSubject.DuplicateAvoidance => aftermath.RulesRead,
            MigrationLossSubject.EnclosedCharacters => aftermath.RowsPastRestoring,
            MigrationLossSubject.DayBoundary => aftermath.RulesNarrowedByDay,
            _ => throw new ArgumentOutOfRangeException(
                nameof(subject),
                subject,
                "A loss exists because something carried is diminished, so the run counts how much of it."),
        };
}
