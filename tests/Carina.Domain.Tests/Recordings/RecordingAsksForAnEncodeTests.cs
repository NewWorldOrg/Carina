using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingAsksForAnEncodeTests
{
    private static readonly DateTime Noon = new(2026, 9, 17, 3, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "a recording that says nothing about encoding asks for one")]
    public void ARecordingThatSaysNothingAsksForAnEncode() => Assert.True(Begun().EncodeWhenRecorded);

    [Fact(DisplayName = "a recording carries what the reservation it was started for asked, because nothing ties the two rows together afterwards")]
    public void ARecordingCarriesWhatItWasStartedWith()
        => Assert.False(Begun(encodeWhenRecorded: false).EncodeWhenRecorded);

    private static Recording Begun(bool encodeWhenRecorded = true)
    {
        var id = RecordingId.New();

        return Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(4001), Noon.AddHours(2)),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".m2ts"),
            Noon.AddHours(2),
            Noon.AddHours(3),
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Noon.AddHours(2),
            null,
            encodeWhenRecorded);
    }

    private static ProgrammeSnapshot Snapshot()
        => new(
            "A programme",
            "What it is about",
            string.Empty,
            [],
            Noon,
            AudioMode.Undetermined,
            ProgrammeSnapshot.SoundsUnannounced);
}
