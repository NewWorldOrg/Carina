using Carina.Domain.Base;
using Carina.Domain.Programmes;

namespace Carina.Domain.Recordings;

public sealed record WindowMove(DateTime EndsAt, bool EndUndecided);

/// <summary>
/// Whether a recording already under way has to hold its tuner longer than it was promised, and
/// until when. A programme that runs later than it said takes the recording with it; a programme
/// that says it will finish sooner does not, because a recording that has already been promised a
/// window is never cut short by the guide changing its mind. A guide that has gone quiet moves
/// nothing either way: the window that was last promised stands, and it is the window that stops
/// the recording.
///
/// A programme that announces no end at all is followed on a horizon of the app's own, renewed
/// while it is still announced. The horizon is renewed once half of it has been spent, so a
/// recording whose end is never announced asks the driver for more time twice a horizon rather
/// than on every tick, and never holds less than half a horizon in hand.
/// </summary>
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
