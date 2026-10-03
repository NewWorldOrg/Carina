namespace Carina.Domain.Captions;

/// <summary>
/// A caption drawn on the canvas: where it sits, how large it is, and the palette PNG it was drawn as.
/// </summary>
public sealed record CaptionPlacement
{
    public const int FurthestEdge = ushort.MaxValue;

    public CaptionPlacement(int left, int top, int width, int height, ReadOnlyMemory<byte> png)
    {
        Within(left, 0, nameof(left));
        Within(top, 0, nameof(top));
        Within(width, 1, nameof(width));
        Within(height, 1, nameof(height));

        if (png.IsEmpty)
        {
            throw new ArgumentException("A caption that is drawn carries the picture it was drawn as.", nameof(png));
        }

        Left = left;
        Top = top;
        Width = width;
        Height = height;
        Png = png;
    }

    public int Left { get; }

    public int Top { get; }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> Png { get; }

    private static void Within(int measured, int lowest, string name)
    {
        if (measured < lowest || measured > FurthestEdge)
        {
            throw new ArgumentOutOfRangeException(name, measured, "A caption is placed and measured in two bytes a side.");
        }
    }
}
