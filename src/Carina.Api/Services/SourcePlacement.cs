using Carina.Domain.Playback;
using Carina.Domain.Recordings;

namespace Carina.Api.Services;

public sealed class SourcePlacement(SourcePlacementDomainService placement, ILogger<SourcePlacement> logger)
{
    public async Task<SourceZero?> PlaceAsync(
        RecordingId id,
        PlaybackOffer offer,
        TimeSpan begins,
        string taken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(offer);

        SourceAlignment alignment = await placement.PlaceAsync(offer.Plan, offer.Artefact, begins, cancellationToken);

        if (alignment is { Artefact: { } artefact, ArtefactBegan: { } encoded })
        {
            logger.LogWarning(
                "The {Taken} of recording {Recording} were taken from a file that began at {Kept} s and the "
                + "artefact {Job} was made from one that began at {Encoded} s, so none of them are placed over the artefact.",
                taken,
                id.Wire,
                begins.TotalSeconds,
                artefact.Wire,
                encoded.TotalSeconds);
        }

        return alignment.Zero;
    }
}
