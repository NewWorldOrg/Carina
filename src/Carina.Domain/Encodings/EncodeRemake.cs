namespace Carina.Domain.Encodings;

/// <summary>
/// Why a job made its artefact a second time, coarser, and what it kept. The first run made something larger
/// than the recording; the second is kept and is no larger than the recording, or is kept and still larger, or
/// came out no smaller than the first, or made nothing, and the first is kept.
/// </summary>
public enum EncodeRemake
{
    LargerThanTheSource = 1,

    StillLargerThanTheSource = 2,

    NoSmallerTheSecondTime = 3,
}

/// <summary>
/// What a run encodes the picture at: the profile's rate factor on the processor and its quantiser on the card,
/// or, for the run that makes an artefact again, both <see cref="RemakeCoarser"/> coarser, no coarser than the
/// coarsest.
/// </summary>
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
        => EncodeShapes.Named(encoder) is EncodeEncoder.Vaapi ? Quantiser.Quantiser : RateFactor.RateFactor;
}

public static class EncodeRemakes
{
    /// <summary>
    /// Whether an artefact of <paramref name="made"/> bytes outgrew a recording of <paramref name="source"/>
    /// bytes. A recording whose size is not known, a missing file or an empty one, is outgrown by nothing.
    /// </summary>
    public static bool Outgrew(long made, long source) => source > 0 && made > source;

    /// <summary>
    /// What a job that made a <paramref name="first"/> larger than the recording keeps of it and of a
    /// <paramref name="second"/> made coarser, which is nothing when the second run made no file: the second when
    /// it came out smaller than the first, the first otherwise.
    /// </summary>
    public static EncodeRemake Judge(long source, long first, long? second)
    {
        if (second is not { } made || made >= first)
        {
            return EncodeRemake.NoSmallerTheSecondTime;
        }

        return Outgrew(made, source) ? EncodeRemake.StillLargerThanTheSource : EncodeRemake.LargerThanTheSource;
    }
}
