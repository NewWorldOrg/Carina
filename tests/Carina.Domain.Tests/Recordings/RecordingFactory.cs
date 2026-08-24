using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

internal static class RecordingFactory
{
    public static readonly TimeProvider Clock =
        new FixedTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));

    public static DateTime Now => Clock.GetUtcNow().UtcDateTime;

    public static ProgrammeRef Programme(int eventId = 4001)
        => new(new NetworkId(32736), new ServiceId(1024), new EventId(eventId), Now.AddMinutes(-5));

    public static ProgrammeSnapshot Snapshot()
        => new("A programme", "What it is about", string.Empty, [new ProgrammeGenre(7, 1)], Now);

    public static Recording Started(
        RecordingId? id = null,
        ReservationId? reservationId = null,
        BroadcastGroupKey? groupKey = null,
        BroadcastGroupRole groupRole = BroadcastGroupRole.Standalone)
    {
        RecordingId recordingId = id ?? RecordingId.New();

        return Recording.Begin(
            recordingId,
            reservationId,
            Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(recordingId, ".m2ts"),
            Now.AddMinutes(-5),
            Now.AddMinutes(55),
            Snapshot(),
            groupKey,
            groupRole,
            Now);
    }

    public static OutcomeDetail Fault(RecordingFault fault = RecordingFault.DriverLost)
        => new(fault, null, string.Empty, Now);
}
