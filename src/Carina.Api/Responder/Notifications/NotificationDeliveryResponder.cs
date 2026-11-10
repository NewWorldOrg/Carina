using Carina.Api.Services;
using Carina.Domain.Notifications;

namespace Carina.Api.Responder.Notifications;

public sealed record NotificationDeliveryResponder(
    Guid Id,
    NoticeKind Kind,
    string Title,
    DateTime OccurredAt,
    NotificationDeliveryState State,
    int Attempts,
    DateTime QueuedAt,
    DateTime? SentAt,
    DateTime? NextAttemptAt,
    NoticeFailure? Failure)
{
    public static NotificationDeliveryResponder Of(NoticeOnItsWay row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new NotificationDeliveryResponder(
            row.Delivery.Id.Value,
            row.Notice.Kind,
            row.Notice.Title,
            row.Notice.OccurredAt,
            row.Delivery.State,
            row.Delivery.Attempts,
            row.Delivery.QueuedAt,
            row.Delivery.SentAt,
            row.Delivery.NextAttemptAt,
            row.Delivery.LastFailure);
    }
}

public sealed record NotificationDeliveryListResponder(int PerPage, IReadOnlyList<NotificationDeliveryResponder> Items)
{
    public static NotificationDeliveryListResponder Of(NotificationDeliveriesListed listed)
    {
        ArgumentNullException.ThrowIfNull(listed);

        return new NotificationDeliveryListResponder(
            listed.PerPage,
            [.. listed.Deliveries.Select(NotificationDeliveryResponder.Of)]);
    }
}
