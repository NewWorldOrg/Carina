using Carina.Domain.Base;
using Carina.Domain.Machines;

namespace Carina.Domain.Encodings;

public enum EncodeSwerve
{
    TheCardIsOutOfReach = 1,

    TheCardCannotDoThisCodec = 2,

    TheProcessorCannotDoThisCodec = 3,
}

/// <summary>
/// Which encoder a job will actually run on, worked out against what this machine turned out to
/// be able to do. A shortfall degrades the run and is written down; it never refuses the profile,
/// which was already saved (BR-EV-004). The one refusal is a codec neither side has.
/// </summary>
public sealed record EncodePlan
{
    private EncodePlan(EncodeEncoder? encoder, EncodeSwerve? swerved, EncodeFailure? refused, string note)
    {
        Encoder = encoder;
        Swerved = swerved;
        Refused = refused;
        Note = note;
    }

    public EncodeEncoder? Encoder { get; }

    public EncodeSwerve? Swerved { get; }

    public EncodeFailure? Refused { get; }

    public string Note { get; }

    public bool CanRun => Encoder is not null;

    public static EncodePlan AsAsked(EncodeEncoder encoder)
        => new(EncodeShapes.Named(encoder), null, null, string.Empty);

    public static EncodePlan Swerving(EncodeEncoder to, EncodeSwerve because, string note)
    {
        if (!Enum.IsDefined(because))
        {
            throw new ArgumentOutOfRangeException(
                nameof(because),
                because,
                "A run goes somewhere other than where it was sent for one of the reasons named here.");
        }

        return new EncodePlan(EncodeShapes.Named(to), because, null, ProgrammeNote.Of(note, ProgrammeNote.Longest));
    }

    public static EncodePlan NothingHereCanDoIt(string note)
        => new(null, null, EncodeFailure.CapabilityUnavailable, ProgrammeNote.Of(note, ProgrammeNote.Longest));
}

public static class EncodePlans
{
    public static EncodePlan For(EncodeProfile profile, EncodeEncoder asked, MachineCapabilities can)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(can);

        bool onTheCard = can.Has(OnTheCard(profile.Codec));
        bool onTheProcessor = can.Has(OnTheProcessor(profile.Codec));

        return EncodeShapes.Named(asked) is EncodeEncoder.Vaapi
            ? WhenTheCardWasAskedFor(onTheCard, onTheProcessor, can)
            : WhenTheProcessorWasAskedFor(onTheCard, onTheProcessor, can);
    }

    private static EncodePlan WhenTheCardWasAskedFor(bool onTheCard, bool onTheProcessor, MachineCapabilities can)
    {
        if (onTheCard)
        {
            return EncodePlan.AsAsked(EncodeEncoder.Vaapi);
        }

        if (!onTheProcessor)
        {
            return EncodePlan.NothingHereCanDoIt(Nowhere(can));
        }

        return can.CardIsUsable
            ? EncodePlan.Swerving(
                EncodeEncoder.Software,
                EncodeSwerve.TheCardCannotDoThisCodec,
                "the build on this machine has no encoder for that codec on the card")
            : EncodePlan.Swerving(EncodeEncoder.Software, EncodeSwerve.TheCardIsOutOfReach, can.Note);
    }

    private static EncodePlan WhenTheProcessorWasAskedFor(bool onTheCard, bool onTheProcessor, MachineCapabilities can)
    {
        if (onTheProcessor)
        {
            return EncodePlan.AsAsked(EncodeEncoder.Software);
        }

        return onTheCard
            ? EncodePlan.Swerving(
                EncodeEncoder.Vaapi,
                EncodeSwerve.TheProcessorCannotDoThisCodec,
                "the build on this machine has no encoder for that codec on the processor")
            : EncodePlan.NothingHereCanDoIt(Nowhere(can));
    }

    private static string Nowhere(MachineCapabilities can)
        => can.Note.Length is 0
            ? "neither the processor nor the card on this machine has an encoder for that codec"
            : $"neither the processor nor the card on this machine has an encoder for that codec: {can.Note}";

    private static Faculty OnTheCard(EncodeCodec codec)
        => EncodeShapes.Named(codec) is EncodeCodec.H265 ? Faculty.EncodeH265OnTheCard : Faculty.EncodeH264OnTheCard;

    private static Faculty OnTheProcessor(EncodeCodec codec)
        => EncodeShapes.Named(codec) is EncodeCodec.H265
            ? Faculty.EncodeH265OnTheProcessor
            : Faculty.EncodeH264OnTheProcessor;
}
