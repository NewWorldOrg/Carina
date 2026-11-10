using Carina.Domain.Base;

namespace Carina.Domain.Notifications;

public sealed class NotificationDeliveryId : CommonValueObject<Guid>
{
    public NotificationDeliveryId(Guid value)
        : base(Validated(value))
    {
    }

    public static NotificationDeliveryId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A notification delivery id cannot be empty.", nameof(value));
        }

        return value;
    }
}
