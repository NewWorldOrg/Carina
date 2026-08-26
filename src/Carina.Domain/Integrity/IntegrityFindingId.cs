using Carina.Domain.Base;

namespace Carina.Domain.Integrity;

public sealed class IntegrityFindingId : CommonValueObject<Guid>
{
    public IntegrityFindingId(Guid value)
        : base(Validated(value))
    {
    }

    public static IntegrityFindingId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A finding id cannot be empty.", nameof(value));
        }

        return value;
    }
}
