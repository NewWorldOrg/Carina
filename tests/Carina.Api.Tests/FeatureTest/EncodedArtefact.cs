using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

internal static class EncodedArtefact
{
    public static readonly OutputRoot Shelf = new("shelf");

    public static EncodeProfile Profile(EncodeCodec codec, DateTime definedAt)
        => EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel(codec is EncodeCodec.H264 ? "Viewing" : "Smaller"),
            codec,
            EncodeResolution.AsSource,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            definedAt);

    public static EncodeJob Made(Recording recording, EncodeProfile profile, DateTime endedAt)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(profile);

        return EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording.Id,
            profile.Id,
            EncodeDestinationId.New(),
            Shelf,
            EncodeJobStatus.Completed,
            EncodeJob.FirstAttempt,
            endedAt.AddHours(-1),
            endedAt.AddHours(-1),
            endedAt,
            null,
            EncodeFileName.Artefact(recording.Id, profile.Id),
            null,
            null,
            null,
            null);
    }
}
