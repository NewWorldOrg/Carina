namespace Carina.Domain.Migration;

public sealed class MigrationOmission
{
    private MigrationOmission()
    {
    }

    public MigrationRunId RunId { get; private set; } = null!;

    public MigrationOmissionSubject Subject { get; private set; }

    public MigrationOmissionGround Ground { get; private set; }

    public static MigrationOmission Rehydrate(
        MigrationRunId runId,
        MigrationOmissionSubject subject,
        MigrationOmissionGround ground)
    {
        ArgumentNullException.ThrowIfNull(runId);

        MigrationOmissionSubject named = MigrationOmissionSubjects.Named(subject);

        if (ground != GroundOf(named))
        {
            throw new ArgumentException(
                $"What was not done about {named} is settled by the requirements, not by the run.",
                nameof(ground));
        }

        return new MigrationOmission
        {
            RunId = runId,
            Subject = named,
            Ground = ground,
        };
    }

    public static MigrationOmission For(MigrationRunId runId, MigrationOmissionSubject subject)
        => Rehydrate(runId, subject, GroundOf(MigrationOmissionSubjects.Named(subject)));

    public static IReadOnlyList<MigrationOmission> EveryOne(MigrationRunId runId)
        => [.. MigrationOmissionSubjects.All.Select(subject => For(runId, subject))];

    public static MigrationOmissionGround GroundOf(MigrationOmissionSubject subject)
        => subject switch
        {
            MigrationOmissionSubject.QualityTimeSeries => MigrationOmissionGround.NothingToCarry,
            _ => MigrationOmissionGround.NotMigratedByDesign,
        };
}
