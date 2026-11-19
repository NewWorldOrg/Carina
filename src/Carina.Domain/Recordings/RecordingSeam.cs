namespace Carina.Domain.Recordings;

public sealed record RecordingSeam
{
    public static readonly TimeSpan StillsKeepClearBy = TimeSpan.FromSeconds(3);

    public RecordingSeam(TimeSpan From, TimeSpan Until)
    {
        if (Until < From)
        {
            throw new ArgumentException("A seam ends where it begins or after it.", nameof(Until));
        }

        this.From = From;
        this.Until = Until;
    }

    public TimeSpan From { get; }

    public TimeSpan Until { get; }

    public static TimeSpan KeepClear(TimeSpan at, IReadOnlyList<RecordingSeam> seams)
    {
        ArgumentNullException.ThrowIfNull(seams);

        TimeSpan clear = at;

        foreach (RecordingSeam seam in seams.OrderBy(seam => seam.From))
        {
            if (clear >= seam.From - StillsKeepClearBy && clear <= seam.Until + StillsKeepClearBy)
            {
                clear = seam.Until + StillsKeepClearBy;
            }
        }

        return clear;
    }
}
