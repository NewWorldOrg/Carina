using Carina.Domain.Base;

namespace Carina.Domain.Reservations;

public sealed class ReservationOutcomeId : CommonValueObject<Guid>
{
    public ReservationOutcomeId(Guid value)
        : base(Validated(value))
    {
    }

    public static ReservationOutcomeId New() => new(Guid.NewGuid());

    private static Guid Validated(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A reservation outcome id cannot be empty.", nameof(value));
        }

        return value;
    }
}
