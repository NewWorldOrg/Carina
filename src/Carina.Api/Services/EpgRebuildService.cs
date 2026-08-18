using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Programmes;

namespace Carina.Api.Services;

public sealed record EpgRebuilt(int Discarded, int Generation);

public sealed class EpgRebuildService(
    IProgrammeRepository programmes,
    ICollectionEpochRepository epochs,
    IAtomicWrite writes,
    TimeProvider clock)
{
    public async Task<ServiceResult<EpgRebuilt>> RebuildAsync(CancellationToken cancellationToken)
    {
        DateTime at = clock.GetUtcNow().UtcDateTime;
        EpgRebuilt rebuilt = await writes.AllOrNothingAsync(
            async token =>
            {
                int discarded = await programmes.ForgetEverythingAsync(token);
                CollectionEpoch epoch = await epochs.ReadAsync(at, token);

                epoch.Advance(at);

                await epochs.SaveAsync(epoch, token);

                return new EpgRebuilt(discarded, epoch.Generation);
            },
            cancellationToken);

        return ServiceResult<EpgRebuilt>.Success(rebuilt);
    }
}
