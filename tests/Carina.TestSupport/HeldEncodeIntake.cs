using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.TestSupport;

public sealed class HeldEncodeIntake(HeldRecordings recordings, HeldEncodeJobs jobs) : IEncodeIntakeReader
{
    public Task<IReadOnlyList<RecordingId>> NeverQueuedAsync(int most, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(most, 1);

        IReadOnlyList<RecordingId> waiting =
        [
            .. recordings.Recordings
                .Where(recording => recording.Outcome is { } outcome && EncodeAutoRun.Subject.Contains(outcome))
                .Where(recording => recording.EncodeWhenRecorded)
                .Where(recording => recording.LeftBehindAt is null)
                .Where(recording => !jobs.Jobs.Any(job => job.RecordingId.Equals(recording.Id)))
                .OrderBy(recording => recording.StartedAtActual)
                .ThenBy(recording => recording.Id.Value)
                .Take(most)
                .Select(recording => recording.Id),
        ];

        return Task.FromResult(waiting);
    }
}
