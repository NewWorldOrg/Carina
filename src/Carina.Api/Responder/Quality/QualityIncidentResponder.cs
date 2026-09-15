using Carina.Domain.Quality;

namespace Carina.Api.Responder.Quality;

public sealed record QualityIncidentResponder(
    string Id,
    DateTime DetectedAt,
    QualityThresholdKey Breached,
    SupplySilence? Silence,
    QualitySubjectKind SubjectKind,
    string SubjectKey,
    double Observed,
    double AppliedValue,
    bool AppliedProvisional,
    QualityIncidentOwner Owner,
    bool Restated,
    string? Classification,
    QualityIncidentState State,
    DateTime? NotifiedAt,
    DateTime? ResolvedAt)
{
    public static QualityIncidentResponder Of(QualityIncident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        return new QualityIncidentResponder(
            incident.Id.Value.ToString(),
            incident.DetectedAt,
            incident.Breached,
            incident.Silence,
            incident.Subject.Kind,
            incident.Subject.Key,
            incident.Observed,
            incident.Applied.Current,
            incident.Applied.Provisional,
            incident.Owner,
            incident.Restated,
            incident.Classification,
            incident.State,
            incident.NotifiedAt,
            incident.ResolvedAt);
    }
}

public sealed record QualityIncidentListResponder(
    IReadOnlyList<QualityIncidentResponder> Items,
    int Owned,
    int Restated)
{
    public static QualityIncidentListResponder Of(IReadOnlyList<QualityIncident> incidents)
    {
        ArgumentNullException.ThrowIfNull(incidents);

        return new QualityIncidentListResponder(
            [.. incidents.Select(QualityIncidentResponder.Of)],
            incidents.Count(incident => !incident.Restated),
            incidents.Count(incident => incident.Restated));
    }
}
