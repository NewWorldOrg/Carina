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
    /// Where each gap falls in the recording's file, measured the way a player measures it: the broadcast's own clock
    /// keeps running through a gap, so a later gap lies after the earlier ones rather than closer to the start.
    /// </summary>
    public static IReadOnlyList<RecordingSeam> SeamsIn(DateTime startedAt, IReadOnlyList<RecordingGap> gaps)
    {
        ArgumentNullException.ThrowIfNull(gaps);

        return [.. gaps.Select(gap => new RecordingSeam(gap.From - startedAt, gap.Until - startedAt))];
    }
}
