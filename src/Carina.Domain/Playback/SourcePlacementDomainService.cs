using Carina.Domain.Encodings;

namespace Carina.Domain.Playback;

public sealed class SourcePlacementDomainService(IEncodeJobRepository jobs)
{
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
