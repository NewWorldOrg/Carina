using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Tests.Recordings;

namespace Carina.Domain.Tests.Library;

internal static class LibraryFactory
{
    public static Recording Complete(
        long fileSizeObserved,
        BroadcastGroupKey? groupKey = null,
        BroadcastGroupRole groupRole = BroadcastGroupRole.Standalone)
    {
        Recording recording = RecordingFactory.Started(groupKey: groupKey, groupRole: groupRole);

        recording.Abort(RecordingFactory.Now);
        recording.Settle(RecordingOutcome.Complete, fileSizeObserved, RecordingFactory.Now);

        return recording;
    }

    public static Recording Measured(DropCounters counters, long scrambledPackets)
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(counters, DropTimeline.Unlocated, scrambledPackets, 0, RecordingFactory.Now);
        recording.Abort(RecordingFactory.Now);
        recording.Settle(RecordingOutcome.Complete, 1_000_000, RecordingFactory.Now);

        return recording;
    }
}
