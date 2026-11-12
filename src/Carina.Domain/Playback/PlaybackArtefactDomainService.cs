using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Domain.Playback;

/// <summary>
/// Which encoded artefacts a recording is played from: every job whose artefact stands as the recording's, the one
/// that ended last first, and among those that ended together the one queued last. A job whose profile is not defined
/// is left out, because nothing then says what it was made as.
/// </summary>
public sealed class PlaybackArtefactDomainService(IEncodeJobRepository jobs, IEncodeProfileRepository profiles)
{
    public async Task<PlaybackArtefacts> OfferedAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        EncodeJob[] made =
        [
            .. (await jobs.ListForRecordingAsync(id, cancellationToken))
                .Where(job => job.StandsAsTheArtefact)
                .OrderByDescending(job => job.EndedAt)
                .ThenByDescending(job => job.QueuedAt),
        ];

        if (made.Length is 0)
        {
            return PlaybackArtefacts.None;
        }

        IReadOnlyList<EncodeProfile> defined = await profiles.ListAsync(cancellationToken);
        List<PlaybackArtefact> offered = [];
        List<EncodeJob> withoutAProfile = [];

        foreach (EncodeJob job in made)
        {
            if (defined.FirstOrDefault(profile => profile.Id.Equals(job.ProfileId)) is { } asked)
            {
                offered.Add(new PlaybackArtefact(job, asked.Codec));
            }
            else
            {
                withoutAProfile.Add(job);
            }
        }

        return new PlaybackArtefacts(offered, withoutAProfile);
    }
}
