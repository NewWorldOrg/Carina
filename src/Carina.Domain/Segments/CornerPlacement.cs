namespace Carina.Domain.Segments;

public readonly record struct CornerPlace(bool Kept, long Repeats);

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
