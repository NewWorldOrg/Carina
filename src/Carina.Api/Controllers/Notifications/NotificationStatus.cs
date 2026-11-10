using Carina.Api.Services;

namespace Carina.Api.Controllers.Notifications;

public static class NotificationStatus
{
    public static int Of(NotificationFailure failure)
        => failure switch
        {
            NotificationFailure.Refused => StatusCodes.Status400BadRequest,
            NotificationFailure.NoSuchDestination or NotificationFailure.NoSuchRule => StatusCodes.Status404NotFound,
            NotificationFailure.TooSoon => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status409Conflict,
        };
}
