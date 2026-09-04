using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

public sealed class EncodeProfileId : CommonValueObject<Guid>
{
    public EncodeProfileId(Guid value)
        : base(Validated(value))
    {
    }

    public string Wire => Value.ToString("N");

    public static EncodeProfileId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An encode profile id cannot be empty.", nameof(value));
        }

        return value;
    }
}
