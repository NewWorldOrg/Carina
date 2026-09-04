using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

public sealed class EncodeJobId : CommonValueObject<Guid>
{
    public EncodeJobId(Guid value)
        : base(Validated(value))
    {
    }

    public string Wire => Value.ToString("N");

    public static EncodeJobId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An encode job id cannot be empty.", nameof(value));
        }

        return value;
    }
}
