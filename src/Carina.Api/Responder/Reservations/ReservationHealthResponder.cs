using Carina.Domain.Reservations;

namespace Carina.Api.Responder.Reservations;

public sealed record ReservationHealthResponder(
    DateTime AsOf,
    int Contended,
    int ReceptionUnavailable,
    int EpgDiverged,
    int EpgMissing)
{
    public static ReservationHealthResponder Of(ReservationHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);

        return new ReservationHealthResponder(
            health.AsOf,
            health.Contended,
            health.ReceptionUnavailable,
            health.EpgDiverged,
            health.EpgMissing);
    }
}
