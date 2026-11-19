namespace Carina.Domain.Notifications;

public sealed record NotificationIntakeSettings
{
    public TimeSpan BeforeFirstLook { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan BetweenLooks { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan LooksBack { get; init; } = TimeSpan.FromMinutes(30);
}
