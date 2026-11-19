using Carina.Domain.Encodings;

namespace Carina.Domain.Recordings;

public sealed class RecordingDisposalDomainService(IRecordingDirectory recordings, IRecordingEncodes encodes)
{
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

    public async Task<bool> RowMayGoAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return !await encodes.AnyUnderWayAsync(id, cancellationToken);
    }
}
