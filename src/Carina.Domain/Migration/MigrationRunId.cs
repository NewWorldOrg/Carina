using Carina.Domain.Base;

namespace Carina.Domain.Migration;

public sealed class MigrationRunId : CommonValueObject<Guid>
{
    public MigrationRunId(Guid value)
        : base(Validated(value))
    {
    }

    public static MigrationRunId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A run id cannot be empty.", nameof(value));
        }

        return value;
    }
}
