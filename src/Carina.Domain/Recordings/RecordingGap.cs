using Carina.Domain.Base;

namespace Carina.Domain.Recordings;

/// <summary>
/// A stretch of a broadcast a recording holds nothing of: from the last write before its file was left to the first
/// write of the session that carried on into it.
/// </summary>
public sealed record RecordingGap
{
    public RecordingGap(DateTime From, DateTime Until)
    {
        this.From = UtcTimes.Required(From, nameof(From));
        this.Until = UtcTimes.Required(Until, nameof(Until));

        if (this.Until <= this.From)
        {
            throw new ArgumentException("A gap ends after it begins.", nameof(Until));
        }
    }

    public DateTime From { get; }

    public DateTime Until { get; }

    public TimeSpan Lasts => Until - From;

    /// <summary>
    /// Where each gap falls in what was written: the time from the start of the recording to the gap, less the gaps
    /// before it.
    /// </summary>
    public static IReadOnlyList<TimeSpan> Placed(DateTime startedAt, IReadOnlyList<RecordingGap> gaps)
    {
        ArgumentNullException.ThrowIfNull(gaps);

        List<TimeSpan> placed = [];
        TimeSpan missedBefore = TimeSpan.Zero;

        foreach (RecordingGap gap in gaps)
        {
            placed.Add(gap.From - startedAt - missedBefore);
            missedBefore += gap.Lasts;
        }

        return placed;
    }
}
