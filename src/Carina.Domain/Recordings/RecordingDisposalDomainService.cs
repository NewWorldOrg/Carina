using Carina.Domain.Encodings;

namespace Carina.Domain.Recordings;

/// <summary>
/// Whether a recording may be thrown away: one still being written is stopped first, and one with an encode waiting
/// or running keeps its row and its encodes until that encode has ended.
/// </summary>
public sealed class RecordingDisposalDomainService(IRecordingDirectory recordings, IRecordingEncodes encodes)
{
    /// <summary>
    /// The recording to throw away, or why it stays, before any file of it is removed.
    /// </summary>
    public async Task<RecordingDisposal> WeighAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return RecordingDisposal.Refused(RecordingDisposalRefusal.NoSuchRecording);
        }

        if (recording.IsInFlight)
        {
            return RecordingDisposal.Refused(RecordingDisposalRefusal.StillBeingWritten);
        }

        return await encodes.AnyUnderWayAsync(id, cancellationToken)
            ? RecordingDisposal.Refused(RecordingDisposalRefusal.BeingEncoded)
            : RecordingDisposal.Of(recording);
    }

    /// <summary>
    /// Whether the row of a recording whose files are gone may go with its encodes: not while an encode queued as the
    /// files were being removed is waiting or running.
    /// </summary>
    public async Task<bool> RowMayGoAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return !await encodes.AnyUnderWayAsync(id, cancellationToken);
    }
}
