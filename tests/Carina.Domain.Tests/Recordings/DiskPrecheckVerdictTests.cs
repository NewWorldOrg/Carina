using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class DiskPrecheckVerdictTests
{
    private static readonly DateTime Noon = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AVerdictWithNoShortfallFoundRoom()
    {
        DiskPrecheckVerdict verdict = DiskPrecheckVerdict.Of(null, 10, 20, 1);

        Assert.True(verdict.HasRoom);
        Assert.Equal((Int128)10, verdict.EstimatedBytes);
        Assert.Equal(20L, verdict.FreeBytes);
        Assert.Equal(1, verdict.Weighed);
    }

    [Fact]
    public void APrecheckHasLookedAtOneRecordingAtLeast()
    {
        ArgumentOutOfRangeException refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => DiskPrecheckVerdict.Of(null, 10, 20, 0));

        Assert.Equal("weighed", refusal.ParamName);

        Assert.Equal(
            "weighed",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DiskPrecheckVerdict.Of(null, 10, 20, -1)).ParamName);

        Assert.Equal(1, DiskPrecheckVerdict.Of(null, 10, 20, 1).Weighed);
    }

    [Fact]
    public void ARecordingWeighsNothingAtTheLightest()
    {
        ArgumentOutOfRangeException refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => DiskPrecheckVerdict.Of(null, -1, 20, 1));

        Assert.Equal("estimatedBytes", refusal.ParamName);

        Assert.Equal(Int128.Zero, DiskPrecheckVerdict.Of(null, 0, 20, 1).EstimatedBytes);
    }

    [Fact]
    public void AShortfallIsOneOfTheClassesThisVerdictHolds()
    {
        ArgumentOutOfRangeException refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => DiskPrecheckVerdict.Of((DiskShortfall)0, 10, 20, 1));

        Assert.Equal("shortfall", refusal.ParamName);

        Assert.Equal(
            "shortfall",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DiskPrecheckVerdict.Of((DiskShortfall)7, 10, 20, 1)).ParamName);

        Assert.Equal(
            DiskShortfall.ShortOfTheEstimate,
            DiskPrecheckVerdict.Of(DiskShortfall.ShortOfTheEstimate, 10, 20, 1).Shortfall);
    }

    [Fact]
    public void AShortfallNamesItselfInAClassTheLedgerAlreadyHolds()
    {
        OutcomeDetail detail = DiskPrecheckVerdict
            .Of(DiskShortfall.NoRoomLeft, 10, 20, 3)
            .Detail(Noon);

        Assert.Equal(RecordingFault.RefusedByDiskPrecheck, detail.Fault);
        Assert.Null(detail.TuneFailure);
        Assert.Equal(Noon, detail.NoticedAt);
        Assert.Equal("NoRoomLeft: 3 recordings weigh 10 bytes against 20 free", detail.Note);
        Assert.Contains(detail.Fault, RecordingFaults.ThatCanInterrupt);
    }

    [Fact]
    public void APrecheckThatFoundRoomHasNothingToWriteDown()
    {
        Assert.Throws<InvalidOperationException>(
            () => DiskPrecheckVerdict.Of(null, 10, 20, 1).Detail(Noon));
    }
}
