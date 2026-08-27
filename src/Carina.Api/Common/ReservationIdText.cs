using Carina.Domain.Reservations;

namespace Carina.Api.Common;

public static class ReservationIdText
{
    public const string Description = "A reservation is named by a UUID, and never by one that is all zeroes.";

    public static ReservationId? Read(Guid id) => id == Guid.Empty ? null : new ReservationId(id);
}
