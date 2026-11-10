using Carina.Domain.Base;

namespace Carina.Domain.Notifications;

public sealed class NotificationRuleId : CommonValueObject<Guid>
{
    public NotificationRuleId(Guid value)
        : base(Validated(value))
    {
    }

    public static NotificationRuleId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A notification rule id cannot be empty.", nameof(value));
        }

        return value;
    }
}
