namespace Carina.Infrastructure.Segments;

/// <summary>
/// How long after the start the records are first looked at for a recording to detect, how often they are
/// looked at and how soon after a detection the next is looked for; how many of the station's earlier
/// recordings its logo is learned from, and how many of them are looked through for earlier episodes of the
/// recording's series.
/// </summary>
public sealed record SegmentDetectionSettings
{
    public static readonly SegmentDetectionSettings Default = new();

    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan AfterADetection { get; init; } = TimeSpan.FromSeconds(5);

    public int LogoFrom { get; init; } = 10;

    public int EpisodesAmong { get; init; } = 200;
}
