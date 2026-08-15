using Carina.Domain.Base;

namespace Carina.Domain.Scans;

public sealed class ScanRunAttemptId : CommonValueObject<Guid>
{
    public ScanRunAttemptId(Guid value)
        : base(Validated(value))
    {
    }

    public static ScanRunAttemptId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A scan run attempt id cannot be empty.", nameof(value));
        }

        return value;
    }
}
