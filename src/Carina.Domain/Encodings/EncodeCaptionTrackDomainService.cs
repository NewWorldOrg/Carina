using Carina.Domain.Captions;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

/// <summary>
/// The captions of a recording carried into its artefact as a text track, and when they were taken.
/// </summary>
public sealed record EncodeCaptions(CaptionRecord Record, DateTime MadeAt);

/// <summary>
/// Decides whether a job puts a text track of captions into the artefact it makes, reading the recording and the
/// captions kept for it: only captions taken with their text by now are carried; otherwise the artefact is placed
/// without the track, and the track is put in once they are.
/// </summary>
public sealed class EncodeCaptionTrackDomainService(IRecordingRepository recordings, ICaptionRecords records)
{
    /// <summary>
    /// The captions to carry into the artefact of the recording, or null when there are none to carry yet.
    /// </summary>
    public async Task<EncodeCaptions?> CaptionsToCarryAsync(RecordingId recording, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (await recordings.FindAsync(recording, cancellationToken) is not { CaptionState: CaptionState.Ready, CaptionsMadeAt: { } madeAt })
        {
            return null;
        }

        return await records.ReadAsync(recording, cancellationToken) is { Lines: not null } record
            ? new EncodeCaptions(record, madeAt)
            : null;
    }
}
