using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveRefusalDetailTests
{
    [Theory]
    [InlineData(TuneFailureKind.NoLock)]
    [InlineData(TuneFailureKind.NoData)]
    [InlineData(TuneFailureKind.IncompletePsi)]
    [InlineData(TuneFailureKind.StreamMismatch)]
    public void BrTd004EachOfTheFourWaysATuningFailsIsADetailOfItsOwn(TuneFailureKind kind)
    {
        LiveRefusalDetail detail = LiveRefusalDetail.Of(kind);

        Assert.Equal(kind, detail.TuneFailure);
        Assert.Null(detail.Holder);
        Assert.Equal((byte)kind, detail.Said);
        Assert.True(detail.Fits(LiveRefusal.WouldNotTune));
    }

    [Fact]
    public void BrTd004TheFourAreTheOnlyFourAndNothingElseIsOneOfThem()
    {
        Assert.Equal(
            [TuneFailureKind.NoLock, TuneFailureKind.NoData, TuneFailureKind.IncompletePsi, TuneFailureKind.StreamMismatch],
            Enum.GetValues<TuneFailureKind>());
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveRefusalDetail.Of((TuneFailureKind)99));
    }

    [Theory]
    [InlineData(LiveTunerHolder.ARecording)]
    [InlineData(LiveTunerHolder.AnotherViewer)]
    [InlineData(LiveTunerHolder.TheGuideOrAScan)]
    public void Fr012EachHolderOfATunerIsADetailOfItsOwn(LiveTunerHolder holder)
    {
        LiveRefusalDetail detail = LiveRefusalDetail.Of(holder);

        Assert.Equal(holder, detail.Holder);
        Assert.Null(detail.TuneFailure);
        Assert.Equal((byte)holder, detail.Said);
        Assert.True(detail.Fits(LiveRefusal.NoTunerFree));
    }

    [Fact]
    public void Fr012AHolderNobodyNamedIsNotOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveRefusalDetail.Of((LiveTunerHolder)99));
    }

    [Theory]
    [InlineData(LiveRefusal.NoSuchChannel)]
    [InlineData(LiveRefusal.NoTunerFree)]
    [InlineData(LiveRefusal.DriverUnavailable)]
    [InlineData(LiveRefusal.TooManyAlready)]
    [InlineData(LiveRefusal.TranscoderWouldNotStart)]
    public void BrTd004ATuningFailureFitsNoReasonButTheOneThatCouldNotTune(LiveRefusal refusal)
    {
        Assert.False(LiveRefusalDetail.Of(TuneFailureKind.NoLock).Fits(refusal));
    }

    [Theory]
    [InlineData(LiveRefusal.NoSuchChannel)]
    [InlineData(LiveRefusal.WouldNotTune)]
    [InlineData(LiveRefusal.DriverUnavailable)]
    [InlineData(LiveRefusal.TooManyAlready)]
    [InlineData(LiveRefusal.TranscoderWouldNotStart)]
    public void Fr012AHolderFitsNoReasonButTheOneWithNoTunerFree(LiveRefusal refusal)
    {
        Assert.False(LiveRefusalDetail.Of(LiveTunerHolder.ARecording).Fits(refusal));
    }

    [Theory]
    [InlineData(LiveRefusal.NoSuchChannel)]
    [InlineData(LiveRefusal.NoTunerFree)]
    [InlineData(LiveRefusal.WouldNotTune)]
    [InlineData(LiveRefusal.DriverUnavailable)]
    [InlineData(LiveRefusal.TooManyAlready)]
    [InlineData(LiveRefusal.TranscoderWouldNotStart)]
    public void SayingNothingFitsEveryReasonAndIsWhatAnUnclassifiedRefusalCarries(LiveRefusal refusal)
    {
        Assert.True(LiveRefusalDetail.Unsaid.Fits(refusal));
        Assert.Equal(0, LiveRefusalDetail.Unsaid.Said);
        Assert.Null(LiveRefusalDetail.Unsaid.TuneFailure);
        Assert.Null(LiveRefusalDetail.Unsaid.Holder);
    }

    [Fact]
    public void BrTd004AByteNoTuningFailureWearsIsNotReadAsOne()
    {
        Assert.Null(LiveRefusalDetail.Read(LiveRefusal.WouldNotTune, 5));
        Assert.Null(LiveRefusalDetail.Read(LiveRefusal.WouldNotTune, 0xff));
        Assert.Equal(TuneFailureKind.StreamMismatch, LiveRefusalDetail.Read(LiveRefusal.WouldNotTune, 4)!.TuneFailure);
    }

    [Fact]
    public void Fr012AByteNoHolderWearsIsNotReadAsOne()
    {
        Assert.Null(LiveRefusalDetail.Read(LiveRefusal.NoTunerFree, 4));
        Assert.Equal(LiveTunerHolder.ARecording, LiveRefusalDetail.Read(LiveRefusal.NoTunerFree, 1)!.Holder);
    }

    [Theory]
    [InlineData(LiveRefusal.NoSuchChannel)]
    [InlineData(LiveRefusal.DriverUnavailable)]
    [InlineData(LiveRefusal.TranscoderWouldNotStart)]
    public void AReasonThatTakesNoDetailReadsNothingButZero(LiveRefusal refusal)
    {
        Assert.Same(LiveRefusalDetail.Unsaid, LiveRefusalDetail.Read(refusal, 0));
        Assert.Null(LiveRefusalDetail.Read(refusal, 1));
    }
}
