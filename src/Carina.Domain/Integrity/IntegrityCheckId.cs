using Carina.Domain.Base;

namespace Carina.Domain.Integrity;

public sealed class IntegrityCheckId : CommonValueObject<Guid>
{
    public IntegrityCheckId(Guid value)
        : base(Validated(value))
    {
    }

    public static IntegrityCheckId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A check id cannot be empty.", nameof(value));
        }

        return value;
    }
}
