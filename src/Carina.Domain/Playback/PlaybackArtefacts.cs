using Carina.Domain.Encodings;

namespace Carina.Domain.Playback;

/// <summary>
/// An encode job whose artefact stands for its recording, with the codec its profile names now.
/// </summary>
public sealed record PlaybackArtefact(EncodeJob Job, EncodeCodec ProfileSays);

/// <summary>
/// The artefacts a recording can be played from, the most recently made first, and the jobs whose artefact stands
/// but whose profile is not defined, which are left out.
/// </summary>
public sealed record PlaybackArtefacts(IReadOnlyList<PlaybackArtefact> Offered, IReadOnlyList<EncodeJob> WithoutAProfile)
{
    public static readonly PlaybackArtefacts None = new([], []);
}
