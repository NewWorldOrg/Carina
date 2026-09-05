using Carina.Domain.Library;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Tests.Recordings;

namespace Carina.Domain.Tests.Library;

public sealed class LibraryRecordingSummaryTests
{
    [Fact]
    public void ARecordingThatEndedIsReadIntoARowTheListCanDrawWithoutTouchingADisk()
    {
        Recording recording = LibraryFactory.Complete(1_000_000);

        LibraryRecordingSummary row = LibraryRecordingSummary.Of(recording);

        Assert.Equal(recording.Id, row.Id);
        Assert.Equal(recording.NetworkId, row.NetworkId);
        Assert.Equal(recording.ServiceId, row.ServiceId);
        Assert.Equal(recording.SnapshotName, row.Name);
        Assert.Equal(recording.StartedAtActual, row.StartedAt);
        Assert.Equal(RecordingOutcome.Complete, row.Outcome);
        Assert.Equal(1_000_000, row.FileSizeObserved);
        Assert.Equal(recording.ObservedAt, row.ObservedAt);
        Assert.Equal(ThumbnailState.Pending, row.ThumbnailState);
    }

    [Fact]
    public void ASizeIsAlwaysHandedOverWithTheMomentSomebodyReadItOffTheDisk()
    {
        LibraryRecordingSummary row = LibraryRecordingSummary.Of(LibraryFactory.Complete(20_000_000));

        Assert.NotNull(row.FileSizeObserved);
        Assert.NotNull(row.ObservedAt);
    }

    [Fact]
    public void ARecordingNobodyCountedPacketsForIsUnmeasuredRatherThanGood()
        => Assert.Equal(
            QualityLevel.Unmeasured,
            LibraryRecordingSummary.Of(LibraryFactory.Complete(1_000_000)).Quality);

    [Fact]
    public void TheStandingAPacketCountEarnsIsTheOneTheQualityDomainGivesIt()
    {
        Recording recording = LibraryFactory.Measured(DropCounters.Counted(500, 1_000_000), 0);

        Assert.Equal(
            RecordingQuality.Of(recording.Counters, recording.ScrambledPackets),
            LibraryRecordingSummary.Of(recording).Quality);
    }

    [Fact]
    public void ARecordingStillBeingWrittenIsNotAListRowAtAll()
        => Assert.Throws<ArgumentException>(() => LibraryRecordingSummary.Of(RecordingFactory.Started()));

    [Fact]
    public void ARowNamesWhereTheNextPageCarriesOnFrom()
    {
        LibraryRecordingSummary row = LibraryRecordingSummary.Of(LibraryFactory.Complete(1_000_000));

        Assert.Equal(new RecordingCursor(row.StartedAt, row.Id), row.Cursor);
    }

    [Fact]
    public void ARelaySegmentCarriesTheBroadcastItBelongsToSoTheListCanFoldIt()
    {
        BroadcastGroupKey key = new("relay-1");

        LibraryRecordingSummary row = LibraryRecordingSummary.Of(
            LibraryFactory.Complete(1_000_000, key, BroadcastGroupRole.RelaySegment));

        Assert.Equal(key, row.BroadcastGroupKey);
        Assert.Equal(BroadcastGroupRole.RelaySegment, row.BroadcastGroupRole);
    }
}
