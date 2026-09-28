using Carina.Domain.Channels;

namespace Carina.Domain.Quality;

public sealed record TunerTroubleWatchPlan(
    IReadOnlyList<TunerTrouble> ToOpen,
    IReadOnlyList<QualityIncident> ToResolve);

public static class TunerTroubleWatch
{
    public const double LockedShareOfATroubledTuner = 0;

    /// <summary>
    /// Opens one incident for each tuner's trouble that has none standing under the same kind, and resolves each
    /// standing one whose tuner no longer has that kind of trouble.
    /// </summary>
    public static TunerTroubleWatchPlan Plan(IReadOnlyList<TunerTrouble> troubles, IReadOnlyList<QualityIncident> standing)
    {
        ArgumentNullException.ThrowIfNull(troubles);
        ArgumentNullException.ThrowIfNull(standing);

        List<QualityIncident> watched = [.. standing.Where(Watched)];

        List<TunerTrouble> opening =
        [
            .. troubles.Where(trouble => !watched.Exists(incident => About(incident, trouble))),
        ];

        List<QualityIncident> resolving =
        [
            .. watched.Where(incident => !troubles.Any(trouble => About(incident, trouble))),
        ];

        return new TunerTroubleWatchPlan(opening, resolving);
    }

    /// <summary>
    /// Names each tuner an unsettled incident says is in trouble, with the worst kind of trouble said of it.
    /// </summary>
    public static IReadOnlyDictionary<string, TunerTroubleKind> Troubled(IReadOnlyList<QualityIncident> standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        Dictionary<string, TunerTroubleKind> troubled = new(StringComparer.Ordinal);

        foreach (QualityIncident incident in standing.Where(Watched))
        {
            TunerTroubleKind kind = TunerTroubles.Named(incident.Classification)!.Value;

            if (!troubled.TryGetValue(incident.Subject.Key, out TunerTroubleKind held) || Weight(kind) > Weight(held))
            {
                troubled[incident.Subject.Key] = kind;
            }
        }

        return troubled;
    }

    public static QualityIncident Restate(QualityIncidentId id, TunerTrouble trouble, DateTime at, Threshold applied)
    {
        ArgumentNullException.ThrowIfNull(trouble);

        return QualityIncident.Detect(
            id,
            at,
            QualityThresholdKey.LockRate,
            Subject(trouble),
            LockedShareOfATroubledTuner,
            applied,
            QualityIncidentOwner.Tuner,
            trouble.Classification);
    }

    private static int Weight(TunerTroubleKind kind)
        => kind is TunerTroubleKind.NoLock ? 2 : TunerTroubles.TakesItOutOfService(kind) ? 1 : 0;

    private static QualitySubject Subject(TunerTrouble trouble)
        => QualitySubject.Of(QualitySubjectKind.Tuner, trouble.Tuner.Value);

    private static bool Watched(QualityIncident incident)
        => incident is { Breached: QualityThresholdKey.LockRate, Owner: QualityIncidentOwner.Tuner, HasSettled: false }
           && TunerTroubles.Named(incident.Classification) is not null;

    private static bool About(QualityIncident incident, TunerTrouble trouble)
        => incident.Subject.Equals(Subject(trouble))
           && string.Equals(incident.Classification, trouble.Classification, StringComparison.Ordinal);
}
