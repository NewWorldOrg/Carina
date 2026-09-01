namespace Carina.Domain.Streaming;

public enum VideoCodec
{
    H264 = 1,
}

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
                $"H.264 quantises between {Finest} and {Coarsest}.");
        }

        Quantiser = quantiser;
    }

    public int Quantiser { get; }

    public override string ToString() => $"qp{Quantiser}";
}
