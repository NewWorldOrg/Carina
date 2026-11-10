using Carina.Domain.Base;

namespace Carina.Domain.Notifications;

public sealed class NotificationDestinationId : CommonValueObject<Guid>
{
    public NotificationDestinationId(Guid value)
        : base(Validated(value))
    {
    }

    public static NotificationDestinationId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A notification destination id cannot be empty.", nameof(value));
        }

        return value;
    }
}
