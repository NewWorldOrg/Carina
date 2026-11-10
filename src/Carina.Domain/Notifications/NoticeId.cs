using Carina.Domain.Base;

namespace Carina.Domain.Notifications;

public sealed class NoticeId : CommonValueObject<Guid>
{
    public NoticeId(Guid value)
        : base(Validated(value))
    {
    }

    public static NoticeId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A notice id cannot be empty.", nameof(value));
        }

        return value;
    }
}
