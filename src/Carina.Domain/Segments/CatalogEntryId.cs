using Carina.Domain.Base;

namespace Carina.Domain.Segments;

public sealed class CatalogEntryId : CommonValueObject<Guid>
{
    public CatalogEntryId(Guid value)
        : base(Validated(value))
    {
    }

    public static CatalogEntryId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A catalog entry id cannot be empty.", nameof(value));
        }

        return value;
    }
}
