using Carina.Domain.Base;

namespace Carina.Domain.Migration;

public sealed class MigrationDetailId : CommonValueObject<Guid>
{
    public MigrationDetailId(Guid value)
        : base(Validated(value))
    {
    }

    public static MigrationDetailId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A detail id cannot be empty.", nameof(value));
        }

        return value;
    }
}
