using Carina.Domain.Base;

namespace Carina.Domain.Quality;

public sealed class QualityThresholdChangeId : CommonValueObject<Guid>
{
    public QualityThresholdChangeId(Guid value)
        : base(Validated(value))
    {
    }

    public static QualityThresholdChangeId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A quality threshold change id cannot be empty.", nameof(value));
        }

        return value;
    }
}
