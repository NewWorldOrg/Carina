using Carina.Domain.Base;

namespace Carina.Domain.Notifications;

/// <summary>
/// The one row of the notification settings changed from a screen: how many minutes before a programme begins a
/// notice of it is sent. No row is held until somebody changes it.
/// </summary>
public sealed class NotificationSettings
{
    public const int TheOnlyRow = 1;

    public const int UsualLeadMinutes = 3;

    public static readonly IReadOnlyList<int> LeadMinutesOffered = [0, 1, 3, 5, 10];

    private NotificationSettings()
    {
    }

    public int Id { get; private set; }

    public int NotifyLeadMinutes { get; private set; }

    public DateTime ChangedAt { get; private set; }

    public static NotificationSettings Rehydrate(int id, int notifyLeadMinutes, DateTime changedAt)
    {
        if (id != TheOnlyRow)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "The notification settings are one row.");
        }

        if (!LeadMinutesOffered.Contains(notifyLeadMinutes))
        {
            throw new ArgumentOutOfRangeException(
                nameof(notifyLeadMinutes),
                notifyLeadMinutes,
                $"A notice is sent {string.Join(", ", LeadMinutesOffered)} minutes before the programme.");
        }

        return new NotificationSettings
        {
            Id = id,
            NotifyLeadMinutes = notifyLeadMinutes,
            ChangedAt = UtcTimes.Required(changedAt, nameof(changedAt)),
        };
    }

    public static NotificationSettings Set(int notifyLeadMinutes, DateTime at) => Rehydrate(TheOnlyRow, notifyLeadMinutes, at);

    public static int LeadMinutesOver(NotificationSettings? held) => held?.NotifyLeadMinutes ?? UsualLeadMinutes;
}
