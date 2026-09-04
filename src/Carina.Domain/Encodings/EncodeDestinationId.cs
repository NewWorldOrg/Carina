using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

public sealed class EncodeDestinationId : CommonValueObject<Guid>
{
    public EncodeDestinationId(Guid value)
        : base(Validated(value))
    {
    }

    public string Wire => Value.ToString("N");

    public static EncodeDestinationId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An encode destination id cannot be empty.", nameof(value));
        }

        return value;
    }
}
