namespace Carina.Domain.Programmes;

/// <summary>
/// What the guide shows where programmes of one service share airtime. A station that replaces part of its
/// schedule announces only the new programmes and never withdraws the old ones, so the programme heard most
/// recently stands and the ones last heard before it are superseded. A programme no whole reading has marked
/// counts as heard before every other, two heard at the same moment both stand, an end not yet announced counts
/// as an hour after the start, and a placeholder takes no part. The rows stay; only the guide's reads leave the
/// superseded ones out.
/// </summary>
public static class ProgrammeSupersession
{
    public static readonly TimeSpan LengthWhenTheEndIsNotAnnounced = TimeSpan.FromHours(1);

    public static bool Supersedes(Programme newer, Programme older)
    {
        ArgumentNullException.ThrowIfNull(newer);
        ArgumentNullException.ThrowIfNull(older);

        return !newer.IsShadow
            && !older.IsShadow
            && newer.NetworkId.Equals(older.NetworkId)
            && newer.ServiceId.Equals(older.ServiceId)
            && newer.LastHeardAt is { } heard
            && (older.LastHeardAt is not { } before || heard > before)
            && newer.StartsAt < EndOf(older)
            && EndOf(newer) > older.StartsAt;
    }

    public static IReadOnlyList<Programme> Standing(IReadOnlyList<Programme> held)
    {
        ArgumentNullException.ThrowIfNull(held);

        ILookup<(int Network, int Service), Programme> services =
            held.ToLookup(programme => (programme.NetworkId.Value, programme.ServiceId.Value));

        return
        [
            .. held.Where(programme => !services[(programme.NetworkId.Value, programme.ServiceId.Value)]
                .Any(newer => Supersedes(newer, programme))),
        ];
    }

    private static DateTime EndOf(Programme programme)
        => programme.EndsAt ?? programme.StartsAt + LengthWhenTheEndIsNotAnnounced;
}
