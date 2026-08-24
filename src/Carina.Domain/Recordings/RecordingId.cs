using Carina.Domain.Base;

namespace Carina.Domain.Recordings;

public sealed class RecordingId : CommonValueObject<Guid>
{
    public RecordingId(Guid value)
        : base(Validated(value))
    {
    }

    public string Wire => Value.ToString("N");

    public static RecordingId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A recording id cannot be empty.", nameof(value));
        }

        return value;
    }
}
