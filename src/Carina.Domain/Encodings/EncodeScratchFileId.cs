using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

public sealed class EncodeScratchFileId : CommonValueObject<Guid>
{
    public EncodeScratchFileId(Guid value)
        : base(Validated(value))
    {
    }

    public static EncodeScratchFileId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A scratch file id cannot be empty.", nameof(value));
        }

        return value;
    }
}
