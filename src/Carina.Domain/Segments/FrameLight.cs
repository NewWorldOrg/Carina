namespace Carina.Domain.Segments;

/// <summary>
/// How bright a frame is, the mean of its pixels, and how much it changed, the mean of how far
/// each pixel moved from the frame before; both rounded to the nearest whole step of grey. A frame
/// is looked at shrunk to <see cref="Width"/> by <see cref="Height"/> in grey, one byte a pixel,
/// row after row.
/// </summary>
public readonly record struct FrameLight(byte Brightness, byte Change)
{
    public const int Width = 64;

    public const int Height = 36;

    public const int Pixels = Width * Height;
}
