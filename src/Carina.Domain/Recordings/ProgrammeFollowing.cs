using Carina.Domain.Base;
using Carina.Domain.Programmes;

namespace Carina.Domain.Recordings;

public sealed record WindowMove(DateTime EndsAt, bool EndUndecided);

/// <summary>
/// Whether a recording already under way has to hold its tuner longer than it was promised, and
/// until when.
/// </summary>
/// <remarks>
/// A programme that runs later takes the recording with it; one that says it will finish sooner does
/// not cut the window short, and a guide that has gone quiet moves nothing. A programme that
/// announces no end is followed on a horizon of the app's own, renewed once half of it has been
/// spent while the programme is still announced.
/// </remarks>
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
