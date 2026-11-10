using Carina.Api.Services;

namespace Carina.Api.Responder.Notifications;

public sealed record NotificationRemovalResponder(Guid Id)
{
    public static NotificationRemovalResponder Of(NotificationRemoved removed)
    {
        ArgumentNullException.ThrowIfNull(removed);

        return new NotificationRemovalResponder(removed.Id);
    }
}
