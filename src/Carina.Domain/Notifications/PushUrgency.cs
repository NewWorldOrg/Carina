namespace Carina.Domain.Notifications;

/// <summary>
/// How soon a push service is asked to wake a device for a message.
/// </summary>
public enum PushUrgency
{
    Normal = 1,
    High = 2,
}
