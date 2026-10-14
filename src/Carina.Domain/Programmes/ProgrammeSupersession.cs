namespace Carina.Domain.Programmes;

/// <summary>
/// What the guide shows where programmes of one service share airtime. A station that replaces part of its
/// schedule announces only the new programmes and never withdraws the old ones, so the programme heard most
/// recently stands and the ones last heard before it are superseded. A programme no whole reading has marked
/// counts as heard before every other, two heard at the same moment both stand, an end not yet announced counts
/// as an hour after the start, and a placeholder takes no part. The rows stay; only the guide's reads leave the
/// superseded ones out. A programme in the guide is held against the guide; one already moved to the archive is
/// held against the guide and the archive together, and one moved before the archive kept heard times has none.
/// </summary>
public static class ProgrammeSupersession
{
    public static readonly TimeSpan LengthWhenTheEndIsNotAnnounced = TimeSpan.FromHours(1);

    public static bool Supersedes(Programme newer, Programme older)
    {
        ArgumentNullException.ThrowIfNull(newer);
        ArgumentNullException.ThrowIfNull(older);

        return !newer.IsShadow && !older.IsShadow && Supersedes(Airtime.Of(newer), Airtime.Of(older));
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

    public static IReadOnlyList<ArchivedProgramme> Standing(
        IReadOnlyList<ArchivedProgramme> kept,
        IReadOnlyList<Programme> held)
    {
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(held);

        Airtime[] airtimes =
        [
            .. held.Where(programme => !programme.IsShadow).Select(Airtime.Of),
            .. kept.Select(Airtime.Of),
        ];
        ILookup<(int Network, int Service), Airtime> services =
            airtimes.ToLookup(airtime => (airtime.Network, airtime.Service));

        return
        [
            .. kept.Where(programme => !services[(programme.NetworkId.Value, programme.ServiceId.Value)]
                .Any(newer => Supersedes(newer, Airtime.Of(programme)))),
        ];
    }

    private static bool Supersedes(Airtime newer, Airtime older)
        => newer.Network == older.Network
            && newer.Service == older.Service
            && newer.LastHeardAt is { } heard
            && (older.LastHeardAt is not { } before || heard > before)
            && newer.StartsAt < older.EndsAt
            && newer.EndsAt > older.StartsAt;

    private readonly record struct Airtime(
        int Network,
        int Service,
        DateTime StartsAt,
        DateTime EndsAt,
        DateTime? LastHeardAt)
    {
        public static Airtime Of(Programme programme)
            => new(
                programme.NetworkId.Value,
                programme.ServiceId.Value,
                programme.StartsAt,
                programme.EndsAt ?? programme.StartsAt + LengthWhenTheEndIsNotAnnounced,
                programme.LastHeardAt);

        public static Airtime Of(ArchivedProgramme programme)
            => new(
                programme.NetworkId.Value,
                programme.ServiceId.Value,
                programme.StartsAt,
                programme.EndsAt,
                programme.LastHeardAt);
    }
}
