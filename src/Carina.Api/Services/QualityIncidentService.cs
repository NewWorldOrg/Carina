using Carina.Api.Common;
using Carina.Domain.Quality;

namespace Carina.Api.Services;

public sealed class QualityIncidentService(
    IQualityIncidentRepository incidents,
    ISupplyStandingBoard board)
{
    public async Task<ServiceResult<IReadOnlyList<QualityIncident>>> ListAsync(CancellationToken cancellationToken)
        => ServiceResult<IReadOnlyList<QualityIncident>>.Success(
            await incidents.ListUnsettledAsync(cancellationToken));

    public Task<ServiceResult<SupplyStanding>> SupplyHealthAsync(CancellationToken cancellationToken)
        => Task.FromResult(board.Latest is { } latest
            ? ServiceResult<SupplyStanding>.Success(latest)
            : ServiceResult<SupplyStanding>.Failure(QualitySaying.NoPassYet()));
}
