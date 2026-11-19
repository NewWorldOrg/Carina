using Carina.Domain.Base;
using Carina.Domain.Programmes;

namespace Carina.Domain.Recordings;

public sealed record WindowMove(DateTime EndsAt, bool EndUndecided);

public static class ProgrammeFollowing
{
    public static WindowMove? Next(
        GuideStanding standing,
        DateTime? announcedEnd,
        TimeSpan marginAfter,
        DateTime windowEnd,
        DateTime now,
        TimeSpan undecidedEndAhead)
    {
        if (marginAfter < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(marginAfter),
                marginAfter,
                "A margin runs past the programme, so it is not negative.");
        }

        if (undecidedEndAhead <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(undecidedEndAhead),
                undecidedEndAhead,
                "A horizon for an end nobody has announced is longer than no time at all.");
        }

        DateTime held = UtcTimes.Required(windowEnd, nameof(windowEnd));
        DateTime moment = UtcTimes.Required(now, nameof(now));

        if (standing is not GuideStanding.Announced)
        {
            return null;
        }

        if (announcedEnd is { } announced)
        {
            DateTime asked = UtcTimes.Required(announced, nameof(announcedEnd)) + marginAfter;

            return asked > held ? new WindowMove(asked, false) : null;
        }

        if (held - moment > TimeSpan.FromTicks(undecidedEndAhead.Ticks / 2))
        {
            return null;
        }

        DateTime renewed = moment + undecidedEndAhead;

        return renewed > held ? new WindowMove(renewed, true) : null;
    }
}
