using Carina.Domain.Base;

namespace Carina.Domain.Channels;

public sealed class StationLogo
{
    public const int LargestPicture = 256 * 1024;

    public const int WidestPicture = 4096;

    private StationLogo()
    {
    }

    public NetworkId NetworkId { get; private set; } = null!;

    public LogoId LogoId { get; private set; } = null!;

    public int LogoType { get; private set; }

    public int LogoVersion { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public byte[] Picture { get; private set; } = [];

    public DateTime CollectedAt { get; private set; }

    public int Area => Width * Height;

    public static StationLogo Collect(
        NetworkId networkId,
        LogoId logoId,
        int logoType,
        int logoVersion,
        int width,
        int height,
        byte[] picture,
        DateTime at)
        => Rehydrate(networkId, logoId, logoType, logoVersion, width, height, picture, at);

    public static StationLogo Rehydrate(
        NetworkId networkId,
        LogoId logoId,
        int logoType,
        int logoVersion,
        int width,
        int height,
        byte[] picture,
        DateTime collectedAt)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(logoId);

        return new StationLogo
        {
            NetworkId = networkId,
            LogoId = logoId,
            LogoType = logoType,
            LogoVersion = logoVersion,
            Width = ValidatedSide(width, nameof(width)),
            Height = ValidatedSide(height, nameof(height)),
            Picture = ValidatedPicture(picture),
            CollectedAt = UtcTimes.Required(collectedAt, nameof(collectedAt)),
        };
    }

    public bool Absorb(StationLogo arriving)
    {
        ArgumentNullException.ThrowIfNull(arriving);

        if (arriving.Area < Area || (arriving.Area == Area && arriving.LogoVersion == LogoVersion))
        {
            return false;
        }

        LogoType = arriving.LogoType;
        LogoVersion = arriving.LogoVersion;
        Width = arriving.Width;
        Height = arriving.Height;
        Picture = arriving.Picture;
        CollectedAt = arriving.CollectedAt;

        return true;
    }

    private static int ValidatedSide(int side, string parameterName)
    {
        if (side is < 1 or > WidestPicture)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                side,
                $"A logo measures 1 to {WidestPicture} pixels on a side.");
        }

        return side;
    }

    private static byte[] ValidatedPicture(byte[] picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        if (picture.Length is 0 or > LargestPicture)
        {
            throw new ArgumentException(
                $"A logo picture is 1 to {LargestPicture} bytes, but this one has {picture.Length}.",
                nameof(picture));
        }

        return picture;
    }
}
