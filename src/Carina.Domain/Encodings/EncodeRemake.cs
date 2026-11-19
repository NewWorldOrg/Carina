namespace Carina.Domain.Encodings;

public enum EncodeRemake
{
    LargerThanTheSource = 1,

    StillLargerThanTheSource = 2,

    NoSmallerTheSecondTime = 3,
}

public sealed record EncodeRate
{
    public const int RemakeCoarser = 4;

    private EncodeRate(ConstantRateFactor rateFactor, ConstantQuantiser quantiser)
    {
        RateFactor = rateFactor;
        Quantiser = quantiser;
    }

    public ConstantRateFactor RateFactor { get; }

    public ConstantQuantiser Quantiser { get; }

    public static EncodeRate Of(EncodeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new EncodeRate(profile.SoftwareRateControl, profile.VaapiRateControl);
    }

    public EncodeRate Coarser()
        => new(
            new ConstantRateFactor(Math.Min(RateFactor.RateFactor + RemakeCoarser, ConstantRateFactor.Coarsest)),
            new ConstantQuantiser(Math.Min(Quantiser.Quantiser + RemakeCoarser, ConstantQuantiser.Coarsest)));

    public int On(EncodeEncoder encoder)
        => EncodeEncoders.OnTheCard(encoder) ? Quantiser.Quantiser : RateFactor.RateFactor;

    public bool CanGoCoarserOn(EncodeEncoder encoder) => Coarser().On(encoder) != On(encoder);
}

public static class EncodeRemakes
{
    public static bool Outgrew(long made, long source) => source > 0 && made > source;

    public static EncodeRemake Judge(long source, long first, long? second)
    {
        if (second is not { } made || made >= first)
        {
            return EncodeRemake.NoSmallerTheSecondTime;
        }

        return Outgrew(made, source) ? EncodeRemake.StillLargerThanTheSource : EncodeRemake.LargerThanTheSource;
    }
}
