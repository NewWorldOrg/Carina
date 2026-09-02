using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class CaptionCanvas
{
    private readonly int stride;

    public CaptionCanvas(VideoSize size)
    {
        ArgumentNullException.ThrowIfNull(size);

        if (size.Width > CaptionPicture.FurthestEdge || size.Height > CaptionPicture.FurthestEdge)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The wire measures the canvas in two bytes a side.");
        }

        Size = size;
        stride = size.Width * PalettePng.BytesPerPixel;
    }

    public VideoSize Size { get; }

    public int FrameLength => stride * Size.Height;

    public CaptionPicture? Drawn(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length != FrameLength)
        {
            throw new ArgumentException(
                $"A frame of this canvas is {FrameLength} bytes, and this one is {bgra.Length}.",
                nameof(bgra));
        }

        int left = Size.Width;
        int top = Size.Height;
        int right = -1;
        int bottom = -1;

        for (int row = 0; row < Size.Height; row++)
        {
            ReadOnlySpan<byte> line = bgra.Slice(row * stride, stride);

            for (int column = 0; column < Size.Width; column++)
            {
                if (line[(column * PalettePng.BytesPerPixel) + 3] is 0)
                {
                    continue;
                }

                left = Math.Min(left, column);
                right = Math.Max(right, column);
                top = Math.Min(top, row);
                bottom = row;
            }
        }

        if (right < 0)
        {
            return null;
        }

        int width = right - left + 1;
        int height = bottom - top + 1;
        ReadOnlySpan<byte> cropped = bgra.Slice((top * stride) + (left * PalettePng.BytesPerPixel));

        return new CaptionPicture(left, top, width, height, PalettePng.Encode(cropped, width, height, stride));
    }
}
