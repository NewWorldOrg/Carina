namespace Carina.Infrastructure.Segments;

/// <summary>
/// How long after the start the recordings that have ended are first looked at, how often they are
/// looked at while none is read and while one is, and how many are looked at at once.
/// </summary>
public sealed record LearningBacklogSettings
{
    public static readonly LearningBacklogSettings Default = new();

    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan WhileReading { get; init; } = TimeSpan.FromSeconds(5);

    public int AtMostALook { get; init; } = 50;
}
