using Carina.Domain.Programmes;

namespace Carina.Domain.Notifications;

public sealed record ProgrammeWatchStep(bool Changed, NoticeKind? Tells)
{
    public static ProgrammeWatchStep Nothing { get; } = new(false, null);
}

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
        bool followed = guide.Announced is { } announced && watch.Follow(announced, at);
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
            return new ProgrammeWatchStep(followed, null);
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
        bool changed = guide.Announced is { } announced && watch.Follow(announced, at);

        if (watch.HasMovedSinceTold && !told.Contains(NoticeKind.WatchedProgrammeMoved))
        {
            return new ProgrammeWatchStep(changed, NoticeKind.WatchedProgrammeMoved);
        }

        return IsGoneBeforeItBegins(watch, guide, at) && !told.Contains(NoticeKind.WatchedProgrammeGone)
            ? new ProgrammeWatchStep(changed, NoticeKind.WatchedProgrammeGone)
            : new ProgrammeWatchStep(changed, null);
    }

    private static bool IsGoneBeforeItBegins(ProgrammeWatch watch, WatchedGuide guide, DateTime at)
        => guide.Standing is GuideStanding.NoLongerAnnounced && at < watch.StartsAt;
}
