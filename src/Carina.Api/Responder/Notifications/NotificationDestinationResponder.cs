using Carina.Api.Services;
using Carina.Domain.Notifications;

namespace Carina.Api.Responder.Notifications;

public sealed record NotificationDestinationResponder(
    Guid Id,
    string Name,
    WebhookTarget Kind,
    string Tail,
    bool Enabled,
    bool Readable,
    DateTime? LastResultAt,
    NoticeFailure? LastFailure,
    DateTime CreatedAt,
    DateTime ChangedAt)
{
    public static NotificationDestinationResponder Of(ListedNotificationDestination listed)
    {
        ArgumentNullException.ThrowIfNull(listed);

        NotificationDestination destination = listed.Destination;

        return new NotificationDestinationResponder(
            destination.Id.Value,
            destination.Name.Value,
            destination.Target,
            destination.UrlTail,
            destination.Enabled,
            listed.Readable,
            destination.LastResultAt,
            destination.LastFailure,
            destination.CreatedAt,
            destination.ChangedAt);
    }
}

public sealed record NotificationDestinationListResponder(IReadOnlyList<NotificationDestinationResponder> Items)
{
    public static NotificationDestinationListResponder Of(IReadOnlyList<ListedNotificationDestination> listed)
    {
        ArgumentNullException.ThrowIfNull(listed);

        return new NotificationDestinationListResponder([.. listed.Select(NotificationDestinationResponder.Of)]);
    }
}
