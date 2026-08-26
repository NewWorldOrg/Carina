using Carina.Contracts;

using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class DiskPrecheckTests
{
    private const long TerrestrialHour = 7_425_000_000L;

    private const long SatelliteHalfHour = 2_745_000_000L;

    private static readonly DateTime Noon = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Recorded = new("recorded");

    [Fact]
    public void TheRecordingThatIsStartingIsWeighedOnItsOwn()
    {
        DiskPrecheckVerdict verdict = DiskPrecheck.Weigh(
            Recorded,
            [Room(free: 8_000_000_000L)],
            Starting(),
            [],
            Noon);

        Assert.True(verdict.HasRoom);
        Assert.Null(verdict.Shortfall);
        Assert.Equal((Int128)TerrestrialHour, verdict.EstimatedBytes);
        Assert.Equal(8_000_000_000L, verdict.FreeBytes);
        Assert.Equal(1, verdict.Weighed);
    }

    [Fact]
    public void EveryRecordingStillRunningAddsWhatIsLeftOfIt()
    {
        var running = new RecordingDemand(TunerKind.Satellite, Noon.AddMinutes(-30), Noon.AddMinutes(30));

        DiskPrecheckVerdict verdict = DiskPrecheck.Weigh(
            Recorded,
            [Room(free: 100_000_000_000L)],
            Starting(),
            [running],
            Noon);

        Assert.Equal((Int128)(TerrestrialHour + SatelliteHalfHour), verdict.EstimatedBytes);
        Assert.Equal(2, verdict.Weighed);
    }

    [Fact]
    public void ARecordingWhoseWindowHasPassedAddsNothingAndIsStillCounted()
    {
        var spent = new RecordingDemand(TunerKind.Satellite, Noon.AddHours(-2), Noon.AddHours(-1));

        DiskPrecheckVerdict verdict = DiskPrecheck.Weigh(
            Recorded,
            [Room(free: 100_000_000_000L)],
            Starting(),
            [spent],
            Noon);

        Assert.Equal((Int128)TerrestrialHour, verdict.EstimatedBytes);
        Assert.Equal(2, verdict.Weighed);
    }

    [Fact]
    public void RoomIsEnoughWhenTheEstimateFitsExactly()
    {
        Assert.Null(Against(Room(free: TerrestrialHour)).Shortfall);
        Assert.Equal(DiskShortfall.ShortOfTheEstimate, Against(Room(free: TerrestrialHour - 1)).Shortfall);
        Assert.Null(Against(Room(free: TerrestrialHour + 1)).Shortfall);
    }

    [Fact]
    public void ADriverThatDidNotAnswerIsNotADriverThatNamedNoSuchRoot()
    {
        DiskPrecheckVerdict verdict = DiskPrecheck.Weigh(Recorded, null, Starting(), [], Noon);

        Assert.Equal(DiskShortfall.RootsUnknown, verdict.Shortfall);
        Assert.Equal((Int128)TerrestrialHour, verdict.EstimatedBytes);
        Assert.Equal(0L, verdict.FreeBytes);
        Assert.Equal(1, verdict.Weighed);
    }

    [Fact]
    public void ARootTheDriverDoesNotNameIsNotARootThatIsFull()
    {
        Assert.Equal(
            DiskShortfall.RootUndeclared,
            DiskPrecheck.Weigh(Recorded, [], Starting(), [], Noon).Shortfall);

        Assert.Equal(
            DiskShortfall.RootUndeclared,
            DiskPrecheck.Weigh(
                Recorded,
                [Room(free: 100_000_000_000L) with { Name = "archive" }],
                Starting(),
                [],
                Noon).Shortfall);
    }

    [Fact]
    public void TheNameOfARootIsMatchedLetterForLetter()
    {
        Assert.Equal(
            DiskShortfall.RootUndeclared,
            DiskPrecheck.Weigh(
                Recorded,
                [Room(free: 100_000_000_000L) with { Name = "Recorded" }],
                Starting(),
                [],
                Noon).Shortfall);
    }

    [Fact]
    public void ARootThatCouldNotBeMeasuredIsNotARootThatRefusedTheWrite()
    {
        Assert.Equal(
            DiskShortfall.RootUnmeasured,
            Against(new StorageRootDto { Name = "recorded" }).Shortfall);

        Assert.Equal(
            DiskShortfall.RootUnmeasured,
            Against(Room(free: 100_000_000_000L, total: 0)).Shortfall);

        Assert.Equal(
            DiskShortfall.ShortOfTheEstimate,
            Against(Room(free: 1, total: 1)).Shortfall);
    }

    [Fact]
    public void ARootThatWouldNotTakeAFileIsNamedForThatAndNotForItsRoom()
    {
        Assert.Equal(
            DiskShortfall.RootNotWritable,
            Against(Room(free: 100_000_000_000L, writable: false)).Shortfall);
    }

    [Fact]
    public void ARootWithNothingLeftIsNotMerelyShortForThisRecording()
    {
        Assert.Equal(DiskShortfall.NoRoomLeft, Against(Room(free: 0)).Shortfall);
        Assert.Equal(DiskShortfall.NoRoomLeft, Against(Room(free: -1)).Shortfall);
        Assert.Equal(DiskShortfall.ShortOfTheEstimate, Against(Room(free: 1)).Shortfall);
    }

    [Fact]
    public void WhatTheRootSaidAboutItsRoomIsCarriedIntoTheVerdict()
    {
        Assert.Equal(12_345L, Against(Room(free: 12_345L)).FreeBytes);
    }

    [Fact]
    public void ThereIsNoPrecheckWithoutARootAndARecordingAndTheOnesBesideIt()
    {
        Assert.Equal(
            "root",
            Assert.Throws<ArgumentNullException>(
                () => DiskPrecheck.Weigh(null!, [], Starting(), [], Noon)).ParamName);

        Assert.Equal(
            "starting",
            Assert.Throws<ArgumentNullException>(
                () => DiskPrecheck.Weigh(Recorded, [], null!, [], Noon)).ParamName);

        Assert.Equal(
            "alreadyRunning",
            Assert.Throws<ArgumentNullException>(
                () => DiskPrecheck.Weigh(Recorded, [], Starting(), null!, Noon)).ParamName);
    }

    private static RecordingDemand Starting()
        => new(TunerKind.Terrestrial, Noon, Noon.AddHours(1));

    private static DiskPrecheckVerdict Against(StorageRootDto room)
        => DiskPrecheck.Weigh(Recorded, [room], Starting(), [], Noon);

    private static StorageRootDto Room(long free, long total = 100_000_000_000_000L, bool writable = true)
        => new()
        {
            Name = "recorded",
            FreeBytes = free,
            TotalBytes = total,
            Writable = writable,
        };
}
