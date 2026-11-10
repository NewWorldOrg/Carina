using Carina.Domain.Channels;
using Carina.Domain.Quality;

namespace Carina.Domain.Notifications;

public sealed record QualityNoticeSource(
    Guid IncidentId,
    QualityIncidentOwner Owner,
    QualityThresholdKey Breached,
    QualitySubjectKind Subject,
    double Observed,
    double Threshold,
    TunerTroubleKind? Trouble,
    DateTime DetectedAt,
    DateTime? ResolvedAt) : NoticeSource
{
    public override IEnumerable<NoticeHappening> Happenings()
        => Owner switch
        {
            QualityIncidentOwner.Quality => Measured(),
            QualityIncidentOwner.Tuner => [new NoticeHappening(NoticeKind.DeviceDisconnected, IncidentId, DetectedAt, this)],
            _ => [],
        };

    private IEnumerable<NoticeHappening> Measured()
    {
        yield return new NoticeHappening(NoticeKind.QualityIncidentDetected, IncidentId, DetectedAt, this);

        if (ResolvedAt is { } resolvedAt)
        {
            yield return new NoticeHappening(NoticeKind.QualityIncidentResolved, IncidentId, resolvedAt, this);
        }
    }
}
