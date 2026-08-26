using Carina.Domain.Base;

namespace Carina.Domain.Recordings;

public sealed record RecordingWindow
{
    private RecordingWindow(DateTime start, DateTime end, TimeSpan lead)
    {
        Start = start;
        End = end;
        Lead = lead;
    }

    public DateTime Start { get; }

    public DateTime End { get; }

    public TimeSpan Lead { get; }

    public TimeSpan Length => End - Start;

    public static RecordingWindow Promised(DateTime effectiveStartAt, DateTime effectiveEndAt, TimeSpan tuningLead)
    {
        DateTime asked = UtcTimes.Required(effectiveStartAt, nameof(effectiveStartAt));
        DateTime until = UtcTimes.Required(effectiveEndAt, nameof(effectiveEndAt));

        if (until <= asked)
        {
            throw new ArgumentException(
                "A recording window ends after it starts, and a promise that does not is nothing to record.",
                nameof(effectiveEndAt));
        }

        if (tuningLead < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tuningLead),
                tuningLead,
                "Tuning runs forwards, so the head of the promise it is allowed to spend is not negative.");
        }

        TimeSpan granted = TimeSpan.FromTicks(Math.Min(tuningLead.Ticks, (until - asked).Ticks / 2));

        return new RecordingWindow(asked + granted, until, granted);
    }
}
