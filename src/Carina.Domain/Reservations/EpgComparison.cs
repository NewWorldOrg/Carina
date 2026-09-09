using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

/// <summary>
/// What a reservation says about its broadcast, held against what the guide says now. Only a
/// difference worth telling somebody about is returned: broadcasts slip by seconds all the time and
/// the margins already absorb that, so a mark on every one of them would make the mark worthless.
/// </summary>
public static class EpgComparison
{
    public static readonly TimeSpan Unremarkable = TimeSpan.FromSeconds(60);

    public static IReadOnlyList<EpgDivergence> Of(Reservation reservation, Programme programme, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(programme);

        var found = new List<EpgDivergence>();

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
}
