namespace Carina.Domain.Segments;

/// <summary>
/// Where a picture of the corners goes: whether it is kept, and how many copies of another picture
/// are put in ahead of it.
/// </summary>
public readonly record struct CornerPlace(bool Kept, long Repeats);

/// <summary>
/// Places the pictures of the corners, one a second of the recording's own time, each on the second
/// its time falls in, a time before zero on the first. A picture whose second already has one, or
/// lies before it, is dropped. One that lands further on is kept after a copy for each second it
/// skipped.
/// </summary>
public sealed class CornerPlacement
{
    public long Placed { get; private set; }

    public CornerPlace Place(TimeSpan at)
    {
        long second = Math.Max(at.Ticks, 0) / TimeSpan.TicksPerSecond;

        if (second < Placed)
        {
            return new CornerPlace(false, 0);
        }

        long repeats = second - Placed;
        Placed = second + 1;

        return new CornerPlace(true, repeats);
    }
}
