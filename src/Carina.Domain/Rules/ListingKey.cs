using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Rules;

/// <summary>
/// The listing a reservation or a programme names: the broadcast's identifiers and the start it was announced with.
/// </summary>
public readonly record struct ListingKey(int NetworkId, int ServiceId, int EventId, DateTime StartsAt)
{
    public (int NetworkId, int ServiceId, int EventId) Broadcast => (NetworkId, ServiceId, EventId);

    public static ListingKey Of(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        return new ListingKey(
            reservation.NetworkId.Value,
            reservation.ServiceId.Value,
            reservation.EventId.Value,
            reservation.ProgrammeStartsAt);
    }

    public static ListingKey Of(Programme programme)
    {
        ArgumentNullException.ThrowIfNull(programme);

        return new ListingKey(
            programme.NetworkId.Value,
            programme.ServiceId.Value,
            programme.EventId.Value,
            programme.StartsAt);
    }

    public static ListingKey Of(ProgrammeMatch programme)
    {
        ArgumentNullException.ThrowIfNull(programme);

        return new ListingKey(
            programme.NetworkId.Value,
            programme.ServiceId.Value,
            programme.EventId.Value,
            programme.StartsAt);
    }
}
