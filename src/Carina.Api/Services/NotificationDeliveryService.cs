using Carina.Api.Common;
using Carina.Domain.Notifications;

namespace Carina.Api.Services;

public sealed record NotificationDeliveriesListed(int PerPage, IReadOnlyList<NoticeOnItsWay> Deliveries);

public sealed class NotificationDeliveryService(
    INotificationDeliveryRepository deliveries,
    INotificationDestinationRepository destinations)
{
    private const string Unknown = "No destination is held under that id.";

    public async Task<ServiceResult<NotificationDeliveriesListed, NotificationFailure>> LatestOfAsync(
        NotificationDestinationId destination,
        int? perPage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (await destinations.FindAsync(destination, cancellationToken) is null)
        {
            return ServiceResult<NotificationDeliveriesListed, NotificationFailure>.Failure(
                Unknown,
                NotificationFailure.NoSuchDestination);
        }

        int listed = NotificationDelivery.Listed(perPage);

        return ServiceResult<NotificationDeliveriesListed, NotificationFailure>.Success(
            new NotificationDeliveriesListed(listed, await deliveries.LatestOfAsync(destination, listed, cancellationToken)));
    }
}
