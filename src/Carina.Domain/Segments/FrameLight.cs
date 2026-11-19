namespace Carina.Domain.Segments;

public readonly record struct FrameLight(byte Brightness, byte Change)
{
    public const int Width = 64;

    public const int Height = 36;

    public const int Pixels = Width * Height;
}
