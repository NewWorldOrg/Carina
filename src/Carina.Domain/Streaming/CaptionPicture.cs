namespace Carina.Domain.Streaming;

public sealed record CaptionPicture
{
    public const int FurthestEdge = ushort.MaxValue;

    public CaptionPicture(int left, int top, int width, int height, ReadOnlyMemory<byte> png)
    {
        if (left < 0 || left > FurthestEdge)
        {
            throw new ArgumentOutOfRangeException(nameof(left), left, "A caption sits on the canvas, and the wire places it in two bytes.");
        }

        if (top < 0 || top > FurthestEdge)
        {
            throw new ArgumentOutOfRangeException(nameof(top), top, "A caption sits on the canvas, and the wire places it in two bytes.");
        }

        if (width < 1 || width > FurthestEdge)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "A caption that is drawn is at least one pixel wide, and the wire measures it in two bytes.");
        }

        if (height < 1 || height > FurthestEdge)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "A caption that is drawn is at least one pixel tall, and the wire measures it in two bytes.");
        }

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
}
