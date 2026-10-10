using Carina.Domain.Encodings;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;

namespace Carina.Api.Services;

/// <summary>
/// Where the zero of a source a recording is played from sits on the clock of the recording's file, and how long
/// that source lasts where it says so.
/// </summary>
public sealed record SourceZero(TimeSpan Shift, TimeSpan? Length);

/// <summary>
/// Places what was taken from a recording's file on the source a playback offer plays: the recording itself
/// counts its seconds from where the file's own clock begins, an artefact from where the job that made it began
/// reading plus what it skipped at the head. An artefact made from a file whose clock began elsewhere has nothing
/// placed on it.
/// </summary>
public sealed class SourcePlacement(IEncodeJobRepository jobs, ILogger<SourcePlacement> logger)
{
    public static readonly TimeSpan ClocksAgreeWithin = TimeSpan.FromMilliseconds(1);

    /// <param name="begins">Where the file's own clock began when <paramref name="taken"/> was taken from it.</param>
    /// <param name="taken">What was taken, as the log names it.</param>
    public async Task<SourceZero?> PlaceAsync(
        RecordingId id,
        PlaybackOffer offer,
        TimeSpan begins,
        string taken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(offer);

        if (offer.Plan.Transcodes)
        {
            return new SourceZero(begins, null);
        }

        if (offer.Artefact is not { } artefact
            || await jobs.FindAsync(artefact, cancellationToken) is not { Timeline: { } timeline })
        {
            return null;
        }

        if ((begins - timeline.SourceStart).Duration() >= ClocksAgreeWithin)
        {
            logger.LogWarning(
                "The {Taken} of recording {Recording} was taken from a file that began at {Kept} s and the "
                + "artefact {Job} was made from one that began at {Encoded} s, so none of it is placed over the artefact.",
                taken,
                id.Wire,
                begins.TotalSeconds,
                artefact.Wire,
                timeline.SourceStart.TotalSeconds);

            return null;
        }

        return new SourceZero(timeline.CaptionShift, timeline.ArtefactLength);
    }
}
