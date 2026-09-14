using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Auth;
using Carina.Domain.Events;
using Carina.Domain.Quality;

namespace Carina.Api.Services;

public enum QualityIncidentFailure
{
    NoSuchIncident = 1,

    AlreadySettled = 2,

    NotToldAboutYet = 3,
}

public sealed class QualityIncidentService(
    IQualityIncidentRepository incidents,
    ISupplyStandingBoard board,
    IAppEventPublisher events,
    TimeProvider clock)
{
    public async Task<ServiceResult<IReadOnlyList<QualityIncident>>> ListAsync(
        bool includeAcknowledged,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<QualityIncident> standing = await incidents.ListUnsettledAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<QualityIncident>>.Success(
            includeAcknowledged
                ? standing
                : [.. standing.Where(incident => incident.AcknowledgedAt is null)]);
    }

    public async Task<ServiceResult<QualityIncident, QualityIncidentFailure>> AcknowledgeAsync(
        string id,
        Subject by,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(by);

        if (!Guid.TryParse(id, out Guid named)
            || named == Guid.Empty
            || await incidents.FindAsync(new QualityIncidentId(named), cancellationToken) is not { } incident)
        {
            return ServiceResult<QualityIncident, QualityIncidentFailure>.Failure(
                QualitySaying.NoSuchIncident(),
                QualityIncidentFailure.NoSuchIncident);
        }

        if (incident.AcknowledgedAt is not null)
        {
            return ServiceResult<QualityIncident, QualityIncidentFailure>.Success(incident);
        }

        if (incident.HasSettled)
        {
            return ServiceResult<QualityIncident, QualityIncidentFailure>.Failure(
                QualitySaying.AlreadySettled(),
                QualityIncidentFailure.AlreadySettled);
        }

        if (incident.State is QualityIncidentState.Detected)
        {
            return ServiceResult<QualityIncident, QualityIncidentFailure>.Failure(
                QualitySaying.NotToldAboutYet(),
                QualityIncidentFailure.NotToldAboutYet);
        }

        incident.Acknowledge(clock.GetUtcNow().UtcDateTime, by.Value);

        await incidents.SaveAsync(incident, cancellationToken);

        events.Signal(AppEventName.Quality);

        return ServiceResult<QualityIncident, QualityIncidentFailure>.Success(incident);
    }

    public Task<ServiceResult<SupplyStanding>> SupplyHealthAsync(CancellationToken cancellationToken)
        => Task.FromResult(board.Latest is { } latest
            ? ServiceResult<SupplyStanding>.Success(latest)
            : ServiceResult<SupplyStanding>.Failure(QualitySaying.NoPassYet()));
}
