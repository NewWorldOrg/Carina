using Carina.Contracts;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Recordings;

public sealed class InFlightRecordingTitles(IRecordingRepository recordings) : IRecordingTitles
{
    public async Task<IReadOnlyDictionary<SessionId, string>> TitlesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Recording> writing = await recordings.ListInFlightAsync(cancellationToken);

        return writing
            .Where(recording => recording.SnapshotName.Length > 0)
            .ToDictionary(recording => RecordingSessions.Named(recording.Id), recording => recording.SnapshotName);
    }
}
