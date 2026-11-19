using Carina.Domain.Encodings;

namespace Carina.Domain.Playback;

public sealed record PlaybackArtefact(EncodeJob Job, EncodeCodec ProfileSays);

public sealed record PlaybackArtefacts(IReadOnlyList<PlaybackArtefact> Offered, IReadOnlyList<EncodeJob> WithoutAProfile)
{
    public static readonly PlaybackArtefacts None = new([], []);
}
