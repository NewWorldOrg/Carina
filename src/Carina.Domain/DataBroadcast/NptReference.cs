namespace Carina.Domain.DataBroadcast;

/// <summary>
/// Where the normal play time of a data stream stands against the <see cref="StreamClock"/>: at the moment
/// <see cref="Stc"/> the programme time reads <see cref="Npt"/>, and from there it runs at the rate of the
/// scale's numerator over its denominator.
/// </summary>
public sealed record NptReference
{
    public const long NptModulus = 1L << 33;

    public const int MostScale = 0xFFFF;

    public NptReference(int tag, long stc, long npt, int scaleNumerator, int scaleDenominator)
    {
        if (npt < 0 || npt >= NptModulus)
        {
            throw new ArgumentOutOfRangeException(nameof(npt), npt, "A normal play time is thirty-three bits.");
        }

        Tag = CarouselNumbers.Tag(tag, nameof(tag));
        Stc = stc;
        Npt = npt;
        ScaleNumerator = Scale(scaleNumerator, nameof(scaleNumerator));
        ScaleDenominator = Scale(scaleDenominator, nameof(scaleDenominator));
    }

    public int Tag { get; }

    public long Stc { get; }

    public long Npt { get; }

    public int ScaleNumerator { get; }

    public int ScaleDenominator { get; }

    private static int Scale(int value, string name)
    {
        if (value < 1 || value > MostScale)
        {
            throw new ArgumentOutOfRangeException(name, value, "A scale of the programme time is two bytes and never zero.");
        }

        return value;
    }
}
