namespace Carina.Infrastructure.Segments;

public sealed record LearningFollowSettings
{
    public static readonly LearningFollowSettings Default = new();

    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan WhileCaughtUp { get; init; } = TimeSpan.FromSeconds(5);

    public int ReadBytes { get; init; } = 1 << 20;

    public long HeadBytes { get; init; } = 8 << 20;
}
