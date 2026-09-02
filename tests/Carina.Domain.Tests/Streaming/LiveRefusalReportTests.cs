using System.Threading.Channels;

using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveRefusalReportTests
{
    [Fact]
    public void ARefusalWithoutACeilingIsReportedAsItsReasonAndNothingMore()
    {
        LiveRefusalReport report = LiveRefusalReport.Of(LiveJoin.Refused(LiveRefusal.NoTunerFree, "every tuner is recording."));

        Assert.Equal(LiveRefusal.NoTunerFree, report.Refusal);
        Assert.Null(report.Ceiling);
        Assert.Equal([(byte)LiveRefusal.NoTunerFree, 0, 0, 0, 0], report.ToPayload());
    }

    [Fact]
    public void AFullBudgetIsReportedWithHowFullItIs()
    {
        LiveRefusalReport report = LiveRefusalReport.Of(LiveJoin.Refused(new TranscodeCeiling(300, 4)));

        Assert.Equal(LiveRefusal.TooManyAlready, report.Refusal);
        Assert.Equal(new TranscodeCeiling(300, 4), report.Ceiling);
        Assert.Equal([(byte)LiveRefusal.TooManyAlready, 0x01, 0x2c, 0x00, 0x04], report.ToPayload());
    }

    [Fact]
    public void AReportIsAsLongAsItSaysAndNeitherAPingNorAProgressReport()
    {
        Assert.Equal(LiveRefusalReport.PayloadLength, LiveRefusalReport.Of(LiveJoin.Refused(new TranscodeCeiling(4, 4))).ToPayload().Length);
        Assert.NotEqual(1, LiveRefusalReport.PayloadLength);
        Assert.NotEqual(LiveStartup.PayloadLength, LiveRefusalReport.PayloadLength);
    }

    [Fact]
    public void AViewerThatWasSeatedHasNothingToReport()
    {
        Assert.Throws<ArgumentException>(() => LiveRefusalReport.Of(LiveJoin.Joined(new SeatedSomewhere())));
    }

    [Theory]
    [InlineData(LiveRefusal.NoSuchChannel)]
    [InlineData(LiveRefusal.NoTunerFree)]
    [InlineData(LiveRefusal.WouldNotTune)]
    [InlineData(LiveRefusal.DriverUnavailable)]
    [InlineData(LiveRefusal.TranscoderWouldNotStart)]
    public void WhatIsWrittenIsReadBack(LiveRefusal refusal)
    {
        LiveRefusalReport written = LiveRefusalReport.Of(LiveJoin.Refused(refusal, "held back."));

        LiveRefusalReading read = LiveRefusalReport.Read(written.ToPayload());

        Assert.Null(read.Fault);
        Assert.Equal(refusal, read.Report!.Refusal);
        Assert.Null(read.Report.Ceiling);
    }

    [Fact]
    public void TheCeilingIsReadBackWithItsNumbers()
    {
        LiveRefusalReading read = LiveRefusalReport.Read(LiveRefusalReport.Of(LiveJoin.Refused(new TranscodeCeiling(5, 4))).ToPayload());

        Assert.Null(read.Fault);
        Assert.Equal(LiveRefusal.TooManyAlready, read.Report!.Refusal);
        Assert.Equal(new TranscodeCeiling(5, 4), read.Report.Ceiling);
    }

    [Fact]
    public void ACeilingBeyondWhatTwoBytesHoldIsWrittenAsTheMostTheyHold()
    {
        LiveRefusalReport report = LiveRefusalReport.Of(LiveJoin.Refused(new TranscodeCeiling(70_000, 70_000)));

        Assert.Equal([(byte)LiveRefusal.TooManyAlready, 0xff, 0xff, 0xff, 0xff], report.ToPayload());
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x02 })]
    [InlineData(new byte[] { 0x02, 0, 0, 0 })]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0 })]
    public void SomethingNotAsLongAsAReportIsRefusedAsSuch(byte[] payload)
    {
        Assert.Equal(LiveRefusalFault.NotAsLongAsARefusalReport, LiveRefusalReport.Read(payload).Fault);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x07)]
    [InlineData(0xff)]
    public void AReasonNoViewerIsRefusedForIsRefusedAsSuch(byte reason)
    {
        Assert.Equal(LiveRefusalFault.AReasonNoViewerIsRefusedFor, LiveRefusalReport.Read([reason, 0, 0, 0, 0]).Fault);
    }

    [Fact]
    public void AFullBudgetThatCarriesNoCeilingIsRefusedAsSuch()
    {
        Assert.Equal(
            LiveRefusalFault.AFullBudgetWithoutItsCeiling,
            LiveRefusalReport.Read([(byte)LiveRefusal.TooManyAlready, 0, 0, 0, 0]).Fault);
        Assert.Equal(
            LiveRefusalFault.AFullBudgetWithoutItsCeiling,
            LiveRefusalReport.Read([(byte)LiveRefusal.TooManyAlready, 0, 3, 0, 4]).Fault);
    }

    [Fact]
    public void ACeilingBesideAReasonThatIsNotAFullBudgetIsRefusedAsSuch()
    {
        Assert.Equal(
            LiveRefusalFault.ACeilingWithoutAFullBudget,
            LiveRefusalReport.Read([(byte)LiveRefusal.NoTunerFree, 0, 4, 0, 4]).Fault);
    }

    [Fact]
    public void AReadingIsEitherAReportOrAFault()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveRefusalReading.Broken((LiveRefusalFault)99));
        Assert.Throws<ArgumentNullException>(() => LiveRefusalReading.Read(null!));
    }

    private sealed class SeatedSomewhere : ILiveViewing
    {
        public ChannelReader<LiveFrame> Frames { get; } = Channel.CreateUnbounded<LiveFrame>().Reader;

        public LiveBacklog Backlog => LiveBacklog.Empty;

        public ILiveStartup? Startup => null;

        public ILiveEnding? Ending => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
