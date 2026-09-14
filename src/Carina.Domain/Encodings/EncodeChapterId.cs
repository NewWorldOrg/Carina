using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

public sealed class EncodeChapterId : CommonValueObject<Guid>
{
    public EncodeChapterId(Guid value)
        : base(Validated(value))
    {
    }

    public static EncodeChapterId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A chapter id cannot be empty.", nameof(value));
        }

        return value;
    }
}
