using Carina.Domain.Base;

namespace Carina.Domain.Reservations;

public sealed class ReservationId : CommonValueObject<Guid>
{
    public ReservationId(Guid value)
        : base(Validated(value))
    {
    }

    public static ReservationId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A reservation id cannot be empty.", nameof(value));
        }

        return value;
    }
}
