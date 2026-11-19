using Carina.Domain.Captions;
using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

public sealed record EncodeCaptions(CaptionRecord Record, DateTime MadeAt);

public sealed class EncodeCaptionTrackDomainService(IRecordingRepository recordings, ICaptionRecords records)
{
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
