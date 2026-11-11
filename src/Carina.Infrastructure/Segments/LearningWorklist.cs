using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

public sealed class LearningWorklist(IRecordingRepository recordings) : ILearningWorklist
{
    public async Task<IReadOnlyList<Recording>> BeingRecordedAsync(
        IReadOnlyList<OutputRoot> withinReach,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(withinReach);

        return
        [
            .. (await recordings.ListInFlightAsync(cancellationToken))
                .Where(recording => recording.IsInFlight && withinReach.Contains(recording.OutputRoot))
                .OrderBy(recording => recording.StartedAtActual)
                .ThenBy(recording => recording.Id.Value),
        ];
    }

    public async Task<RecordingStanding?> StandingAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        Recording? found = await recordings.FindAsync(id, cancellationToken);

        if (found is null)
        {
            return null;
        }

        return found.IsInFlight ? RecordingStanding.InFlight : RecordingStanding.Ended;
    }
}
