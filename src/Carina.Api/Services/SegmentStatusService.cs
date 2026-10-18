using Carina.Api.Common;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

/// <summary>
/// Where CM, OP and ED detection stands, as the settings screen shows it.
/// </summary>
public sealed record SegmentStatus(LearningDataAmount LearningData, CatalogCounts Catalog);

public sealed class SegmentStatusService(ILearningDataAmountReader learningData, ICatalogCountsReader catalog)
{
    public async Task<ServiceResult<SegmentStatus>> ReadAsync(CancellationToken cancellationToken)
        => ServiceResult<SegmentStatus>.Success(new SegmentStatus(
            await learningData.ReadAsync(cancellationToken),
            await catalog.ReadAsync(cancellationToken)));
}
