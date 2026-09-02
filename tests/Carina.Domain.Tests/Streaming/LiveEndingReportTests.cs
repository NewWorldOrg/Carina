using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveEndingReportTests
{
    [Fact]
    public void AnEndingIsReportedAsItsMarkAndItsReason()
    {
        LiveEndingReport report = LiveEndingReport.Of(LiveSupplyEnding.Of(LiveSupplyEnd.TakenForARecording, "a recording outranked it."));

        Assert.Equal(LiveSupplyEnd.TakenForARecording, report.Why);
        Assert.Equal([LiveEndingReport.Mark, (byte)LiveSupplyEnd.TakenForARecording], report.ToPayload());
    }

    [Fact]
    public void AReportIsAsLongAsItSaysAndNeitherAPingNorARefusalNorAProgressReport()
    {
        Assert.Equal(LiveEndingReport.PayloadLength, LiveEndingReport.Of(LiveSupplyEnding.Of(LiveSupplyEnd.LetGo, "let go.")).ToPayload().Length);
        Assert.NotEqual(1, LiveEndingReport.PayloadLength);
        Assert.NotEqual(LiveRefusalReport.PayloadLength, LiveEndingReport.PayloadLength);
        Assert.NotEqual(LiveStartup.PayloadLength, LiveEndingReport.PayloadLength);
    }

    [Fact]
    public void TheMarkIsNotAControlMessageNorAReasonNorAProgressState()
    {
        Assert.False(Enum.IsDefined((LiveControl)LiveEndingReport.Mark));
        Assert.False(Enum.IsDefined((LiveRefusal)LiveEndingReport.Mark));
        Assert.False(Enum.IsDefined((LiveSupplyEnd)LiveEndingReport.Mark));
        Assert.NotEqual(0, LiveEndingReport.Mark);
        Assert.NotEqual(1, LiveEndingReport.Mark);
    }

    [Theory]
    [InlineData(LiveSupplyEnd.LetGo)]
    [InlineData(LiveSupplyEnd.TakenForARecording)]
    [InlineData(LiveSupplyEnd.DriverDraining)]
    [InlineData(LiveSupplyEnd.WindowClosed)]
    [InlineData(LiveSupplyEnd.TunerFailed)]
    [InlineData(LiveSupplyEnd.StoppedByAnother)]
    [InlineData(LiveSupplyEnd.DriverLost)]
    public void WhatIsWrittenIsReadBack(LiveSupplyEnd why)
    {
        LiveEndingReading read = LiveEndingReport.Read(LiveEndingReport.Of(LiveSupplyEnding.Of(why, "because.")).ToPayload());

        Assert.Null(read.Fault);
        Assert.Equal(why, read.Report!.Why);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xe0 })]
    [InlineData(new byte[] { 0xe0, 0x02, 0x00 })]
    public void SomethingNotAsLongAsAReportIsRefusedAsSuch(byte[] payload)
    {
        Assert.Equal(LiveEndingFault.NotAsLongAsAnEndingReport, LiveEndingReport.Read(payload).Fault);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x01)]
    [InlineData(0xff)]
    public void SomethingNotMarkedAsAReportIsRefusedAsSuch(byte mark)
    {
        Assert.Equal(LiveEndingFault.NotMarkedAsAnEndingReport, LiveEndingReport.Read([mark, 0x02]).Fault);
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x08)]
    [InlineData(0xff)]
    public void AReasonNoSupplyEndsForIsRefusedAsSuch(byte why)
    {
        Assert.Equal(LiveEndingFault.AReasonNoSupplyEndsFor, LiveEndingReport.Read([LiveEndingReport.Mark, why]).Fault);
    }

    [Fact]
    public void AReadingIsEitherAReportOrAFault()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveEndingReading.Broken((LiveEndingFault)99));
        Assert.Throws<ArgumentNullException>(() => LiveEndingReading.Read(null!));
        Assert.Throws<ArgumentNullException>(() => LiveEndingReport.Of(null!));
    }
}
