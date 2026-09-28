using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

/// <summary>
/// What a reservation says about its broadcast, held against what the guide says now. Only a
/// difference beyond what the margins already absorb is returned.
/// </summary>
public static class EpgComparison
{
    public static readonly TimeSpan Unremarkable = TimeSpan.FromSeconds(60);

    public static IReadOnlyList<EpgDivergence> Of(Reservation reservation, Programme programme, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(programme);

        var found = new List<EpgDivergence>();

        if (!reservation.NetworkId.Equals(programme.NetworkId) || !reservation.ServiceId.Equals(programme.ServiceId))
        {
            found.Add(new EpgDivergence(
                DivergedField.Service,
                Carried(reservation.NetworkId.Value, reservation.ServiceId.Value),
                Carried(programme.NetworkId.Value, programme.ServiceId.Value),
                at));
        }

        if (Moved(reservation.ProgrammeStartsAt, programme.StartsAt))
        {
            found.Add(new EpgDivergence(
                DivergedField.StartAt,
                Said(reservation.ProgrammeStartsAt),
                Said(programme.StartsAt),
                at));
        }

        if (Moved(reservation.EndAt, EndOf(programme)))
        {
            found.Add(new EpgDivergence(
                DivergedField.EndAt,
                Said(reservation.EndAt),
                Said(EndOf(programme)),
                at));
        }

        if (programme.Name.Length > 0 && !string.Equals(reservation.SnapshotName, programme.Name, StringComparison.Ordinal))
        {
            found.Add(new EpgDivergence(DivergedField.Name, reservation.SnapshotName, programme.Name, at));
        }

        return found;
    }

    public static DateTime EndOf(Programme programme)
    {
        ArgumentNullException.ThrowIfNull(programme);

        return programme.EndsAt
               ?? programme.StartsAt + Reservation.ProvisionalLengthWhenTheEndIsNotAnnounced;
    }

    private static bool Moved(DateTime held, DateTime announced)
        => (announced - held).Duration() > Unremarkable;

    private static string Said(DateTime moment) => moment.ToString("O");

    private static string Carried(int networkId, int serviceId) => $"{networkId}-{serviceId}";
}
