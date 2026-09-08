namespace Carina.Domain.Migration;

public sealed class MigrationOmission
{
    private MigrationOmission()
    {
    }

    public MigrationRunId RunId { get; private set; } = null!;

    public MigrationOmissionSubject Subject { get; private set; }

    public MigrationOmissionGround Ground { get; private set; }

    public int? Affected { get; private set; }

    public static MigrationOmission Rehydrate(
        MigrationRunId runId,
        MigrationOmissionSubject subject,
        MigrationOmissionGround ground,
        int? affected)
    {
        ArgumentNullException.ThrowIfNull(runId);

        MigrationOmissionSubject named = MigrationOmissionSubjects.Named(subject);

        if (ground != GroundOf(named))
        {
            throw new ArgumentException(
                $"What was not done about {named} is settled by the requirements, not by the run.",
                nameof(ground));
        }

        if (MigrationOmissionSubjects.CountsRows(named) != affected.HasValue)
        {
            throw new ArgumentException(
                $"What was not done about {named} is written down with a count of what it touched, or without "
                + "one, and which of the two is settled by the requirements.",
                nameof(affected));
        }

        if (affected is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(affected), affected, "A run counts nothing negative.");
        }

        return new MigrationOmission
        {
            RunId = runId,
            Subject = named,
            Ground = ground,
            Affected = affected,
        };
    }

    public static MigrationOmission For(MigrationRunId runId, MigrationOmissionSubject subject, int? affected)
        => Rehydrate(runId, subject, GroundOf(MigrationOmissionSubjects.Named(subject)), affected);

    public static IReadOnlyList<MigrationOmission> EveryOne(MigrationRunId runId, MigrationAftermath aftermath)
    {
        ArgumentNullException.ThrowIfNull(aftermath);

        return
        [
            .. MigrationOmissionSubjects.All.Select(subject => For(runId, subject, Counting(subject, aftermath))),
        ];
    }

    public static MigrationOmissionGround GroundOf(MigrationOmissionSubject subject)
        => subject switch
        {
            MigrationOmissionSubject.QualityTimeSeries => MigrationOmissionGround.NothingToCarry,
            _ => MigrationOmissionGround.NotMigratedByDesign,
        };

    private static int? Counting(MigrationOmissionSubject subject, MigrationAftermath aftermath)
        => subject switch
        {
            MigrationOmissionSubject.DuplicateAvoidance => aftermath.RulesRead,
            MigrationOmissionSubject.EnclosedCharacters => aftermath.RowsPastRestoring,
            _ => null,
        };
}
