using Carina.Domain.Encodings;

namespace Carina.Domain.Migration;

public sealed class MigrationStanding
{
    private MigrationStanding()
    {
    }

    public MigrationRunId RunId { get; private set; } = null!;

    public MigrationStandingSubject Subject { get; private set; }

    public MigrationFinding Finding { get; private set; }

    public bool WouldStopARunForReal => MigrationFindings.WouldStopARunForReal(Finding);

    public static MigrationStanding Rehydrate(
        MigrationRunId runId,
        MigrationStandingSubject subject,
        MigrationFinding finding)
    {
        ArgumentNullException.ThrowIfNull(runId);

        MigrationStandingSubject named = MigrationStandingSubjects.Named(subject);

        if (!MigrationFindings.Under(named).Contains(MigrationFindings.Named(finding)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(finding),
                finding,
                $"'{finding}' is not something a run can come back with about {named}.");
        }

        return new MigrationStanding
        {
            RunId = runId,
            Subject = named,
            Finding = finding,
        };
    }

    public static IReadOnlyList<MigrationStanding> EveryOne(
        MigrationRunId runId,
        MigrationRootStanding newRoot,
        EncodeUnaskedStanding whereEncodesGo)
        =>
        [
            Rehydrate(runId, MigrationStandingSubject.TheNewRoot, MigrationFindings.Of(newRoot)),
            Rehydrate(runId, MigrationStandingSubject.WhereEncodesGo, MigrationFindings.Of(whereEncodesGo)),
        ];
}
