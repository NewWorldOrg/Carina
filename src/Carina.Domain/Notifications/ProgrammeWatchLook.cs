using Carina.Domain.Programmes;

namespace Carina.Domain.Notifications;

/// <summary>
/// What one look did to a programme asked to be told of: whether the row changed, and what to tell of it now.
/// </summary>
public sealed record ProgrammeWatchStep(bool Changed, NoticeKind? Tells)
{
    public static ProgrammeWatchStep Nothing { get; } = new(false, null);
}

/// <summary>
/// One look at a programme asked to be told of, by the time and by what the guide says.
/// </summary>
/// <remarks>
/// A row that waits follows the guide, is told of once its minutes before the start are reached, is passed over
/// when its broadcast has ended, and is cancelled when the guide stops naming it before it begins. A row already
/// told of stays as it is; a start that moves, or a guide that stops naming it before it begins, is told once.
/// </remarks>
public static class ProgrammeWatchLook
{
    public static ProgrammeWatchStep At(
        ProgrammeWatch watch,
        WatchedGuide guide,
        IReadOnlySet<NoticeKind> told,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(watch);
        ArgumentNullException.ThrowIfNull(guide);
        ArgumentNullException.ThrowIfNull(told);

        return watch.State switch
        {
            ProgrammeWatchState.Waiting => Waiting(watch, guide, at),
            ProgrammeWatchState.Notified when at < watch.EndsAt => Notified(watch, guide, told, at),
            _ => ProgrammeWatchStep.Nothing,
        };
    }

    private static ProgrammeWatchStep Waiting(ProgrammeWatch watch, WatchedGuide guide, DateTime at)
    {
        WatchedBroadcast before = Held(watch);

        if (guide.Announced is { } announced)
        {
            watch.Follow(announced, at);
        }

        ProgrammeWatchMoment moment = watch.MomentAt(at);

        if (moment is ProgrammeWatchMoment.Over)
        {
            watch.Skip(at);

            return new ProgrammeWatchStep(true, null);
        }

        if (IsGoneBeforeItBegins(watch, guide, at))
        {
            watch.Cancel(at);

            return new ProgrammeWatchStep(true, NoticeKind.WatchedProgrammeGone);
        }

        if (moment is ProgrammeWatchMoment.NotYet)
        {
            return new ProgrammeWatchStep(Held(watch) != before, null);
        }

        watch.Notify(at);

        return new ProgrammeWatchStep(true, NoticeKind.ProgrammeStarting);
    }

    private static ProgrammeWatchStep Notified(
        ProgrammeWatch watch,
        WatchedGuide guide,
        IReadOnlySet<NoticeKind> told,
        DateTime at)
    {
        WatchedBroadcast before = Held(watch);
        bool moved = guide.Announced is { } announced && watch.Follow(announced, at);
        bool changed = Held(watch) != before;

        if (moved && !told.Contains(NoticeKind.WatchedProgrammeMoved))
        {
            return new ProgrammeWatchStep(changed, NoticeKind.WatchedProgrammeMoved);
        }

        return IsGoneBeforeItBegins(watch, guide, at) && !told.Contains(NoticeKind.WatchedProgrammeGone)
            ? new ProgrammeWatchStep(changed, NoticeKind.WatchedProgrammeGone)
            : new ProgrammeWatchStep(changed, null);
    }

    private static bool IsGoneBeforeItBegins(ProgrammeWatch watch, WatchedGuide guide, DateTime at)
        => guide.Standing is GuideStanding.NoLongerAnnounced && at < watch.StartsAt;

    private static WatchedBroadcast Held(ProgrammeWatch watch)
        => new(watch.StartsAt, watch.EndsAt, watch.ProgrammeName, watch.StationName);
}
