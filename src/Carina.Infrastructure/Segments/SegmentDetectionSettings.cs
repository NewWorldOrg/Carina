namespace Carina.Infrastructure.Segments;

public sealed record SegmentDetectionSettings
{
    public static readonly SegmentDetectionSettings Default = new();

    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan AfterADetection { get; init; } = TimeSpan.FromSeconds(5);

    public int LogoFrom { get; init; } = 10;

    public int EpisodesAmong { get; init; } = 200;
}
