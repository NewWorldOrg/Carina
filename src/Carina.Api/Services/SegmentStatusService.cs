using Carina.Api.Common;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

/// <summary>
/// Where CM, OP and ED detection stands, as the settings screen shows it.
/// </summary>
public sealed record SegmentStatus(LearningDataAmount LearningData);

public sealed class SegmentStatusService(ILearningDataAmountReader learningData)
{
    public async Task<ServiceResult<SegmentStatus>> ReadAsync(CancellationToken cancellationToken)
        => ServiceResult<SegmentStatus>.Success(new SegmentStatus(await learningData.ReadAsync(cancellationToken)));
}
