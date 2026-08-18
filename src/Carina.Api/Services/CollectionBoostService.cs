using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;

namespace Carina.Api.Services;

public sealed record BoostOutcome(BoostStarted? Started, BoostVerdict? Refusal, bool NothingMatched);

public sealed class CollectionBoostService(CollectionBoost boost)
{
    public async Task<ServiceResult<BoostOutcome>> StartAsync(
        CollectNowRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        BoostVerdict verdict = boost.MayStart();

        if (!verdict.IsAllowed)
        {
            return ServiceResult<BoostOutcome>.Success(new BoostOutcome(null, verdict, false));
        }

        BoostStarted? started = await boost.StartAsync(Wanted(request), cancellationToken);

        if (started is null)
        {
            return ServiceResult<BoostOutcome>.Success(
                new BoostOutcome(null, boost.MayStart(), false));
        }

        return started.Streams == 0
            ? ServiceResult<BoostOutcome>.Success(new BoostOutcome(null, null, true))
            : ServiceResult<BoostOutcome>.Success(new BoostOutcome(started, null, false));
    }

    private static Func<BroadcastStream, bool> Wanted(CollectNowRequest request)
        => stream =>
            (request.NetworkId is not { } network || stream.NetworkId.Value == network)
            && (request.TransportStreamId is not { } carried || stream.TransportStreamId.Value == carried)
            && (request.ServiceId is not { } service
                || stream.Services.Any(carriedService => carriedService.Value == service));
}
