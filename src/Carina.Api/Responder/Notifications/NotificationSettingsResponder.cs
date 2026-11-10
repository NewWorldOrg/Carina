using Carina.Api.Services;

namespace Carina.Api.Responder.Notifications;

public sealed record NotificationSettingsResponder(int NotifyLeadMinutes, bool PublicOriginKnown)
{
    public static NotificationSettingsResponder Of(NotificationSettingsRead read)
    {
        ArgumentNullException.ThrowIfNull(read);

        return new NotificationSettingsResponder(read.NotifyLeadMinutes, read.PublicOriginKnown);
    }
}
