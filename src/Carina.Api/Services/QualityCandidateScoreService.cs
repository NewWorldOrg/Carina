using Carina.Api.Common;
using Carina.Domain.Channels;

namespace Carina.Api.Services;

public sealed class QualityCandidateScoreService(ICandidateChannelRepository candidates)
{
    public async Task<ServiceResult<IReadOnlyList<CandidateChannel>>> ListAsync(CancellationToken cancellationToken)
        => ServiceResult<IReadOnlyList<CandidateChannel>>.Success(await candidates.ListAllAsync(cancellationToken));
}
