using Carina.Domain.Channels;

namespace Carina.Domain.Quality;

public sealed record LockWatchPlan(
    IReadOnlyList<TunerFault> ToOpen,
    IReadOnlyList<QualityIncident> ToResolve);

public static class LockWatch
{
    public const double LockedShareOfATunerThatCannotLock = 0;

    /// <summary>
    /// Opens one incident for each tuner that cannot lock and has none standing, and resolves each
    /// standing one whose tuner is no longer among those that cannot lock.
    /// </summary>
    public static LockWatchPlan Plan(IReadOnlyList<TunerFault> cannotLock, IReadOnlyList<QualityIncident> standing)
    {
        ArgumentNullException.ThrowIfNull(cannotLock);
        ArgumentNullException.ThrowIfNull(standing);

        List<QualityIncident> watched = [.. standing.Where(Watched)];

        List<TunerFault> opening =
        [
            .. cannotLock.Where(fault => !watched.Exists(incident => About(incident, fault))),
        ];

        List<QualityIncident> resolving =
        [
            .. watched.Where(incident => !cannotLock.Any(fault => About(incident, fault))),
        ];

        return new LockWatchPlan(opening, resolving);
    }

    public static QualityIncident Restate(QualityIncidentId id, TunerFault fault, DateTime at, Threshold applied)
    {
        ArgumentNullException.ThrowIfNull(fault);

        return QualityIncident.Detect(
            id,
            at,
            QualityThresholdKey.LockRate,
            Subject(fault),
            LockedShareOfATunerThatCannotLock,
            applied,
            QualityIncidentOwner.Tuner,
            fault.Classification);
    }

    private static QualitySubject Subject(TunerFault fault)
        => QualitySubject.Of(QualitySubjectKind.Tuner, fault.Tuner.Value);

    private static bool Watched(QualityIncident incident)
        => incident is { Breached: QualityThresholdKey.LockRate, Owner: QualityIncidentOwner.Tuner, HasSettled: false }
           && string.Equals(incident.Classification, TunerFaults.CannotLockClassification, StringComparison.Ordinal);

    private static bool About(QualityIncident incident, TunerFault fault)
        => incident.Subject.Equals(Subject(fault));
}
