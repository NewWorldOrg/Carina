using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

public sealed record SegmentTrainingSettings
{
    public static readonly SegmentTrainingSettings Default = new();

    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromMinutes(10);

    public SegmentSearch Search { get; init; } = SegmentSearch.Default;
}
