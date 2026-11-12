using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingCarriedOverTests
{
    private static readonly DateTime Began = new(2026, 9, 8, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Ended = new(2026, 9, 8, 4, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime CarriedAt = new(2026, 9, 8, 5, 0, 0, DateTimeKind.Utc);

    private static readonly ProgrammeRef Programme =
        new(new NetworkId(32736), new ServiceId(1024), new EventId(4321), Began);

    [Fact(DisplayName = "a recording carried over arrives as one recorded in full, over the times the source system recorded it")]
    public void ARecordingCarriedOverArrivesRecordedInFullOverItsOwnTimes()
    {
        Recording carried = Carried();

        Assert.Equal(RecordingOutcome.Complete, carried.Outcome);
        Assert.False(carried.IsInFlight);
        Assert.Equal(Began, carried.StartedAtActual);
        Assert.Equal(Ended, carried.StoppedAtActual);
        Assert.Equal(Began, carried.ExpectedWindowStart);
        Assert.Equal(Ended, carried.ExpectedWindowEnd);
        Assert.Equal(Ended, carried.PromisedWindowEnd);
        Assert.Equal((long)(Ended - Began).TotalMilliseconds, carried.WrittenDurationMs);
        Assert.Equal(Ended, carried.AbortedAt);
    }

    [Fact(DisplayName = "BR-MD-005: a recording carried over holds no reservation, no tuner and no mark of where it came from")]
    public void ARecordingCarriedOverHoldsNoMemoryOfWhereItCameFrom()
    {
        Recording carried = Carried();

        Assert.Null(carried.ReservationId);
        Assert.Null(carried.TunerDeviceId);
        Assert.Equal(BroadcastGroupRole.Standalone, carried.BroadcastGroupRole);
        Assert.Equal(0, carried.ResumeCount);
        Assert.Empty(carried.Interruptions);
    }

    [Fact(DisplayName = "BR-MD-006: a recording carried over is unmeasured and waits for its picture to be made from the file")]
    public void ARecordingCarriedOverIsUnmeasuredAndAwaitsItsPicture()
    {
        Recording carried = Carried();

        Assert.False(carried.CcMeasured);
        Assert.Null(carried.CcDroppedPackets);
        Assert.Null(carried.CcTotalPackets);
        Assert.Null(carried.MeasuredUpdatedAt);
        Assert.Equal(ThumbnailState.Pending, carried.ThumbnailState);
        Assert.True(carried.AwaitsAThumbnail);
    }

    [Fact(DisplayName = "a recording carried over keeps the size its file was seen at, the moment it was carried, and the source system's title alone")]
    public void ARecordingCarriedOverKeepsItsSizeAndTitle()
    {
        Recording carried = Carried();

        Assert.Equal(100, carried.FileSizeObserved);
        Assert.Equal(CarriedAt, carried.ObservedAt);
        Assert.Equal(Programme, carried.Programme);
        Assert.Equal("a programme", carried.SnapshotName);
        Assert.Equal(string.Empty, carried.SnapshotSummary);
        Assert.Equal(string.Empty, carried.SnapshotExtended);
        Assert.Empty(carried.SnapshotGenres);
        Assert.Equal(AudioMode.Undetermined, carried.SnapshotAudio);
        Assert.Equal(ProgrammeSnapshot.SoundsUnannounced, carried.SnapshotSounds);
    }

    [Fact(DisplayName = "a recording carried over lives in the new root under the name that carries its own id")]
    public void ARecordingCarriedOverLivesUnderItsOwnName()
    {
        RecordingId id = RecordingId.New();
        RecordingFileName name = RecordingFileName.For(id, ".m2ts");

        Recording carried = Recording.CarriedOver(
            id,
            Programme,
            "a programme",
            new OutputRoot("carried"),
            name,
            100,
            Ended,
            CarriedAt);

        Assert.Equal(id, carried.Id);
        Assert.Equal(new OutputRoot("carried"), carried.OutputRoot);
        Assert.Equal(name, carried.FileName);
    }

    [Fact(DisplayName = "a recording that ends on the instant it starts cannot be carried over as one")]
    public void ARecordingThatEndsAsItStartsCannotBeCarriedOver()
    {
        RecordingId id = RecordingId.New();

        Assert.Throws<ArgumentException>(() => Recording.CarriedOver(
            id,
            Programme,
            "a programme",
            new OutputRoot("carried"),
            RecordingFileName.For(id, ".m2ts"),
            100,
            Began,
            CarriedAt));
    }

    private static Recording Carried()
    {
        RecordingId id = RecordingId.New();

        return Recording.CarriedOver(
            id,
            Programme,
            "a programme",
            new OutputRoot("carried"),
            RecordingFileName.For(id, ".m2ts"),
            100,
            Ended,
            CarriedAt);
    }
}
