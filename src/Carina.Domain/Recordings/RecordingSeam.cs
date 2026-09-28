namespace Carina.Domain.Recordings;

/// <summary>
/// Where a gap sits in a recording's file: the position its last write before the gap reached, and the position
/// the session that carried on began writing at.
/// </summary>
public sealed record RecordingSeam
{
    /// <summary>
    /// How far on either side of a seam a still picture is not taken from. It is wider than a group of pictures, so
    /// the key frame cut short by the gap and the pictures that lean on it are both passed over.
    /// </summary>
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

    /// <summary>
    /// The position a still picture is taken from instead of <paramref name="at"/>: past the end of any seam it falls
    /// near, and <paramref name="at"/> itself when it falls near none.
    /// </summary>
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
