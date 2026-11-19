namespace Carina.Infrastructure.Segments;

public sealed record LearningBacklogSettings
{
    public static readonly LearningBacklogSettings Default = new();

    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan WhileReading { get; init; } = TimeSpan.FromSeconds(5);

    public int AtMostALook { get; init; } = 50;
}
