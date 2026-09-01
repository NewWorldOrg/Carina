namespace Carina.Domain.Streaming;

public enum ScanType
{
    Undetermined = 0,

    Progressive = 1,

    Interlaced = 2,
}

public enum AudioMode
{
    Undetermined = 0,

    Mono = 1,

    Stereo = 2,

    DualMono = 3,

    Surround = 4,
}

public sealed record VideoSize
{
    public VideoSize(int width, int height)
    {
        if (width < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "A picture is at least one pixel wide.");
        }

        if (height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "A picture is at least one pixel tall.");
        }

        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public override string ToString() => $"{Width}x{Height}";
}

public sealed record FrameRate
{
    public static readonly FrameRate BroadcastFrames = new(30000, 1001);

    private FrameRate(int numerator, int denominator)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    public int Numerator { get; }

    public int Denominator { get; }

    public double PerSecond => (double)Numerator / Denominator;

    public static FrameRate Of(int numerator, int denominator)
    {
        if (numerator < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numerator),
                numerator,
                "A stream that carries no frames a second has no frame rate to name.");
        }

        if (denominator < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(denominator),
                denominator,
                "A frame rate is a ratio, and nothing is divided by nothing.");
        }

        return new FrameRate(numerator, denominator);
    }

    public static FrameRate? Read(string? text)
    {
        if (text is null)
        {
            return null;
        }

        int slash = text.IndexOf('/', StringComparison.Ordinal);

        if (slash < 1)
        {
            return null;
        }

        if (!int.TryParse(text[..slash], out int numerator) || !int.TryParse(text[(slash + 1)..], out int denominator))
        {
            return null;
        }

        return numerator > 0 && denominator > 0 ? new FrameRate(numerator, denominator) : null;
    }

    public override string ToString() => $"{Numerator}/{Denominator}";
}

public sealed record StreamAttributes
{
    public static readonly StreamAttributes SafeSide = new(
        new VideoSize(1440, 1080),
        ScanType.Interlaced,
        FrameRate.BroadcastFrames,
        AudioMode.Stereo);

    public StreamAttributes(VideoSize size, ScanType scan, FrameRate rate, AudioMode audio)
    {
        ArgumentNullException.ThrowIfNull(size);
        ArgumentNullException.ThrowIfNull(rate);

        if (!Enum.IsDefined(scan))
        {
            throw new ArgumentOutOfRangeException(nameof(scan), scan, "A picture is scanned one of the ways named here.");
        }

        if (!Enum.IsDefined(audio))
        {
            throw new ArgumentOutOfRangeException(nameof(audio), audio, "Sound arrives in one of the modes named here.");
        }

        Size = size;
        Scan = scan;
        Rate = rate;
        Audio = audio;
    }

    public VideoSize Size { get; }

    public ScanType Scan { get; }

    public FrameRate Rate { get; }

    public AudioMode Audio { get; }
}
