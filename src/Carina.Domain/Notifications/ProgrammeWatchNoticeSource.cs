namespace Carina.Domain.Notifications;

/// <summary>
/// A programme asked to be told of, as it stood when one thing about it was told: that it begins, that its time
/// moved, or that the guide no longer names it.
/// </summary>
public sealed record ProgrammeWatchNoticeSource(
    NoticeKind Told,
    Guid WatchId,
    int NetworkId,
    int ServiceId,
    string ProgrammeName,
    string? Station,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime ToldAt) : NoticeSource
{
    public static ProgrammeWatchNoticeSource Of(ProgrammeWatch watch, NoticeKind told, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(watch);

        return new ProgrammeWatchNoticeSource(
            told,
            watch.Id.Value,
            watch.NetworkId.Value,
            watch.ServiceId.Value,
            watch.ProgrammeName,
            watch.StationName,
            watch.StartsAt,
            watch.EndsAt,
            at);
    }

    public NoticeHappening Happening()
    {
        if (!NoticeKinds.OfAWatchedProgramme.Contains(Told))
        {
            throw new InvalidOperationException($"A programme asked to be told of is not told of as {Told}.");
        }

        return new NoticeHappening(Told, WatchId, ToldAt, this);
    }

    public override IEnumerable<NoticeHappening> Happenings() => [Happening()];
}
