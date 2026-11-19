using Carina.Domain.Base;

namespace Carina.Domain.Reservations;

public sealed record ReservationHealth(
    DateTime AsOf,
    int Contended,
    int ReceptionUnavailable,
    int EpgDiverged,
    int EpgMissing)
{
    public DateTime AsOf { get; } = UtcTimes.Required(AsOf, nameof(AsOf));

    public int Contended { get; } = Counted(Contended, nameof(Contended));

    public int ReceptionUnavailable { get; } = Counted(ReceptionUnavailable, nameof(ReceptionUnavailable));

    public int EpgDiverged { get; } = Counted(EpgDiverged, nameof(EpgDiverged));

    public int EpgMissing { get; } = Counted(EpgMissing, nameof(EpgMissing));

    public static ReservationHealth Clear(DateTime asOf) => new(asOf, 0, 0, 0, 0);

    private static int Counted(int count, string name)
        => count >= 0
            ? count
            : throw new ArgumentOutOfRangeException(name, count, "A count of reservations is never negative.");
}
