namespace Carina.Domain.Notifications;

/// <summary>
/// How often the ledgers are read for what is worth telling, and how far back each look reads.
/// </summary>
public sealed record NotificationIntakeSettings
{
    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan LooksBack { get; init; } = TimeSpan.FromMinutes(30);
}
