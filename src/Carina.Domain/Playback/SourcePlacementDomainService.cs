using Carina.Domain.Encodings;

namespace Carina.Domain.Playback;

/// <summary>
/// Places what was taken from a recording's file on the source a playback plan plays: the recording itself counts
/// its seconds from where the file's own clock begins, an artefact from where the job that made it began reading plus
/// what it skipped at the head. An artefact made from a file whose clock began elsewhere has nothing placed on it.
/// </summary>
public sealed class SourcePlacementDomainService(IEncodeJobRepository jobs)
{
    /// <param name="artefact">The job whose artefact the plan hands over, if it hands one over.</param>
    /// <param name="begins">Where the file's own clock began when what is placed was taken from it.</param>
    public async Task<SourceAlignment> PlaceAsync(
        PlaybackPlan plan,
        EncodeJobId? artefact,
        TimeSpan begins,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Transcodes)
        {
            return SourceAlignment.At(new SourceZero(begins, null));
        }

        if (artefact is null || await jobs.FindAsync(artefact, cancellationToken) is not { Timeline: { } timeline })
        {
            return SourceAlignment.Unplaced;
        }

        return timeline.WasMadeFromAFileBeginningAt(begins)
            ? SourceAlignment.At(new SourceZero(timeline.CaptionShift, timeline.ArtefactLength))
            : SourceAlignment.ClocksDisagree(artefact, timeline.SourceStart);
    }
}
