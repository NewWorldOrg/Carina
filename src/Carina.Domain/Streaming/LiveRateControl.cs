namespace Carina.Domain.Streaming;

public sealed record BitrateCap
{
    public BitrateCap(int kilobitsPerSecond)
    {
        if (kilobitsPerSecond < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kilobitsPerSecond),
                kilobitsPerSecond,
                "A ceiling of nothing a second is not a ceiling.");
        }

        KilobitsPerSecond = kilobitsPerSecond;
    }

    public int KilobitsPerSecond { get; }

    public override string ToString() => $"{KilobitsPerSecond}k";
}

public sealed record ConstantQuantiser
{
    public const int Finest = 0;

    public const int Coarsest = 51;

    public ConstantQuantiser(int quantiser)
    {
        if (quantiser is < Finest or > Coarsest)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantiser),
                quantiser,
                $"H.264 and H.265 quantise between {Finest} and {Coarsest}.");
        }

        Quantiser = quantiser;
    }

    public int Quantiser { get; }

    public override string ToString() => $"qp{Quantiser}";
}

/// <summary>
/// How finely the card quantises an AV1 picture, from the finest to the coarsest index the format has.
/// </summary>
public sealed record QualityIndex
{
    public const int Finest = 0;

    public const int Coarsest = 255;

    public QualityIndex(int index)
    {
        if (index is < Finest or > Coarsest)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"AV1 quantises between {Finest} and {Coarsest}.");
        }

        Index = index;
    }

    public int Index { get; }

    public override string ToString() => $"q{Index}";
}

/// <summary>
/// What the card is handed for one profile in each format, set so that the profile looks alike in all three.
/// </summary>
public sealed record CardRateControl
{
    public CardRateControl(ConstantQuantiser h264, ConstantQuantiser h265, QualityIndex av1)
    {
        ArgumentNullException.ThrowIfNull(h264);
        ArgumentNullException.ThrowIfNull(h265);
        ArgumentNullException.ThrowIfNull(av1);

        H264 = h264;
        H265 = h265;
        Av1 = av1;
    }

    public ConstantQuantiser H264 { get; }

    public ConstantQuantiser H265 { get; }

    public QualityIndex Av1 { get; }
}
