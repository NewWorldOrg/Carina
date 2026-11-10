using Carina.Domain.Base;

namespace Carina.Domain.Notifications;

public sealed class ProgrammeWatchId : CommonValueObject<Guid>
{
    public ProgrammeWatchId(Guid value)
        : base(Validated(value))
    {
    }

    public static ProgrammeWatchId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The id of a programme asked to be told of cannot be empty.", nameof(value));
        }

        return value;
    }
}
