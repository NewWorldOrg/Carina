using System.Runtime.Versioning;

using Carina.Api.Responder.Recordings;
using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Programmes;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

using Puppis.Contracts;

namespace Carina.Api.Tests.Unit;

[SupportedOSPlatform("linux")]
public sealed class InjectedDropsReachTheDetailTests
{
    private const int LostPackets = 3;

    private const int InjectedAtSecond = 7;

    private static readonly DateTime Noon = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheSecondADropWasCountedAtIsTheSecondTheDetailPutsItAt()
    {
        DropTimeline placed = Measured(new DropPositionsDto(
            0,
            [new DropBucketDto(InjectedAtSecond, LostPackets, 0)],
            []));

        Assert.Equal(InjectedAtSecond, Assert.Single(placed.Buckets).Second);

        RecordingDetailResponder detail = Detail(placed, dropped: LostPackets);
        DropBucketResponder shown = Assert.Single(detail.Positions.Buckets);

        Assert.True(detail.Positions.Located);
        Assert.Equal(InjectedAtSecond, shown.Second);
        Assert.Equal(LostPackets, shown.Continuity);
        Assert.Equal(0, shown.Scrambled);
    }

    [Fact]
    public void AStreamNothingWasCountedInPutsNoMarkOnTheDetail()
    {
        DropTimeline placed = Measured(new DropPositionsDto(0, [], []));

        RecordingDetailResponder detail = Detail(placed, dropped: 0);

        Assert.True(detail.Positions.Located);
        Assert.Empty(detail.Positions.Buckets);
    }

    private static DropTimeline Measured(DropPositionsDto positions)
        => DropTimeline.Rehydrate(
            positions.AnchorPcr,
            [.. positions.Buckets.Select(bucket => new DropBucket(bucket.Second, bucket.Continuity, bucket.Scrambled))],
            [.. positions.Reanchors.Select(reanchor => new PcrReanchor(reanchor.Second, reanchor.Before, reanchor.After))]);

    private static RecordingDetailResponder Detail(DropTimeline placed, long dropped)
    {
        RecordingId id = RecordingId.New();
        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(4001), Noon),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            Noon,
            Noon.AddHours(1),
            new ProgrammeSnapshot(
                "A programme",
                "What it is about",
                string.Empty,
                [],
                Noon,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            Noon,
            new TunerDeviceId("pt3-0"));

        recording.Measure(DropCounters.Counted(dropped, 5), placed, 0, 0, Noon);

        return RecordingDetailResponder.Of(
            new RecordingSeen(
                recording,
                EncodeStanding.NotEncoded,
                QualityThresholdStanding.Bands(QualityThresholdStanding.Over([], Noon))));
    }
}
