namespace Carina.Domain.Notifications;

/// <summary>
/// How often the notices that wait are sent, how many one destination is sent in a window, and how long a notice is
/// worth sending and worth keeping.
/// </summary>
public sealed record NotificationDeliverySettings
{
    public TimeSpan BeforeFirstRound { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan BetweenRounds { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(60);

    public int MostInAWindow { get; init; } = 30;

    public int MostTrialsOfOneInAWindow { get; init; } = 1;

    public int MostTrialsInAWindow { get; init; } = 5;

    public TimeSpan TooOldAfter { get; init; } = TimeSpan.FromHours(1);

    public TimeSpan Keeps { get; init; } = TimeSpan.FromDays(14);

    public TimeSpan BetweenPurges { get; init; } = TimeSpan.FromHours(1);
}
