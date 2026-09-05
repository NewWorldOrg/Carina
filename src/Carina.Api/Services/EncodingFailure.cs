using Carina.Domain.Encodings;

namespace Carina.Api.Services;

public enum EncodingFailure
{
    Refused = 1,

    DriverUnreachable = 2,

    DriverRefused = 3,

    NoSuchProfile = 4,

    NoSuchDestination = 5,

    NoSuchRecording = 6,

    NoSuchJob = 7,

    RecordingStillBeingWritten = 8,

    RecordingFailed = 9,

    AlreadyInTheQueue = 10,

    AlreadyEncoded = 11,

    AlreadyOver = 12,

    MovedMeanwhile = 13,
}

public static class EncodeRefusals
{
    public static string Describe(IReadOnlyList<EncodeRefusal> refusals)
    {
        ArgumentNullException.ThrowIfNull(refusals);

        return string.Join(" ", refusals.Select(Describe));
    }

    public static string Describe(EncodeRefusal refusal) => refusal switch
    {
        EncodeRefusal.CodecUnknown => $"codec: one of {string.Join(", ", Enum.GetNames<EncodeCodec>())}.",
        EncodeRefusal.ResolutionUnknown => $"resolution: one of {string.Join(", ", Enum.GetNames<EncodeResolution>())}.",
        EncodeRefusal.DeinterlaceUnknown => $"deinterlace: one of {string.Join(", ", Enum.GetNames<Deinterlace>())}.",
        EncodeRefusal.RateFactorOutOfRange =>
            $"rateFactor: a constant rate factor between {ConstantRateFactor.Finest} and {ConstantRateFactor.Coarsest}.",
        EncodeRefusal.QuantiserOutOfRange =>
            $"quantiser: a constant quantiser between {ConstantQuantiser.Finest} and {ConstantQuantiser.Coarsest}.",
        EncodeRefusal.LabelMissing => "label: a name a person reads, and not an empty one.",
        EncodeRefusal.LabelTooLong => $"label: at most {EncodeLabel.Longest} characters.",
        EncodeRefusal.OutputRootNotDeclared => "outputRoot: the name of a root the storage surface declares.",
        EncodeRefusal.OutputRootNotHeld =>
            "outputRoot: a root this process holds for writing; the roots the recordings are read from take no artefact.",
        EncodeRefusal.DefaultProfileUnknown => "defaultProfileId: the id of a profile that is defined.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A save is refused for one of the reasons named."),
    };
}
