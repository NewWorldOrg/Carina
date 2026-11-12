namespace Carina.Domain.Notifications;

/// <summary>
/// What became of a set of rules when one destination was taken out of it.
/// </summary>
public enum NotificationRuleNarrowing
{
    NotNamed = 1,

    Narrowed = 2,

    LeftWithNone = 3,
}
