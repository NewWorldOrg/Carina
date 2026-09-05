namespace Carina.Domain.Channels;

public sealed record LogoSweepSettings
{
    public TimeSpan BetweenSweeps { get; init; } = TimeSpan.FromHours(1);

    public TimeSpan LongestVisit { get; init; } = TimeSpan.FromMinutes(6);

    public TimeSpan BetweenVisits { get; init; } = TimeSpan.FromDays(30);

    public TimeSpan BeforeRetrying { get; init; } = TimeSpan.FromHours(6);

    public bool Collects { get; init; } = true;
}
