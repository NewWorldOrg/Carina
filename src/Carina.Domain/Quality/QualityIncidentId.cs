using Carina.Domain.Base;

namespace Carina.Domain.Quality;

public sealed class QualityIncidentId : CommonValueObject<Guid>
{
    public QualityIncidentId(Guid value)
        : base(Validated(value))
    {
    }

    public static QualityIncidentId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A quality incident id cannot be empty.", nameof(value));
        }

        return value;
    }
}
