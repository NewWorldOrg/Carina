namespace Carina.Domain.Scans;

public sealed class ScanRunId : CommonValueObject<Guid>
{
    public ScanRunId(Guid value)
        : base(Validated(value))
    {
    }

    public static ScanRunId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A scan run id cannot be empty.", nameof(value));
        }

        return value;
    }
}
