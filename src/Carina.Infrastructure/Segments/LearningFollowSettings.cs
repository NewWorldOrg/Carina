namespace Carina.Infrastructure.Segments;

/// <summary>
/// How often recordings are looked at for following, how long a follow that has read to the end of a
/// file still being written waits before reading on, and how much it reads at once.
/// </summary>
public sealed record LearningFollowSettings
{
    public static readonly LearningFollowSettings Default = new();

    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan WhileCaughtUp { get; init; } = TimeSpan.FromSeconds(1);

    public int ReadBytes { get; init; } = 1 << 20;
}
