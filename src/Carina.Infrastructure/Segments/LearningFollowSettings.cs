namespace Carina.Infrastructure.Segments;

/// <summary>
/// How often recordings are looked at for following, how long a follow that has read to the end of a
/// file still being written waits before reading on, how much it reads at once, and how much of the
/// head of a file still being written it waits for before reading where the file's clock begins.
/// </summary>
public sealed record LearningFollowSettings
{
    public static readonly LearningFollowSettings Default = new();

    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan WhileCaughtUp { get; init; } = TimeSpan.FromSeconds(5);

    public int ReadBytes { get; init; } = 1 << 20;

    /// <summary>
    /// More than ffprobe reads from a file's head to find where its clock begins, so a file still being
    /// written gives the answer the whole file will.
    /// </summary>
    public long HeadBytes { get; init; } = 8 << 20;
}
