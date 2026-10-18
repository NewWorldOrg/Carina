using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// How long after the start spare time is first looked for the day's round of learning, how often it is looked
/// for, and the settings learning tries.
/// </summary>
public sealed record SegmentTrainingSettings
{
    public static readonly SegmentTrainingSettings Default = new();

    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromMinutes(10);

    public SegmentSearch Search { get; init; } = SegmentSearch.Default;
}
