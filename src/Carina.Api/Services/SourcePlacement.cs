using Carina.Domain.Playback;
using Carina.Domain.Recordings;

namespace Carina.Api.Services;

/// <summary>
/// Places what was taken from a recording's file on the source a playback offer plays, and says in the log when an
/// artefact was made from a file whose clock began elsewhere.
/// </summary>
public sealed class SourcePlacement(SourcePlacementDomainService placement, ILogger<SourcePlacement> logger)
{
    /// <param name="begins">Where the file's own clock began when <paramref name="taken"/> was taken from it.</param>
    /// <param name="taken">What was taken, named in the plural as the log names it.</param>
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
