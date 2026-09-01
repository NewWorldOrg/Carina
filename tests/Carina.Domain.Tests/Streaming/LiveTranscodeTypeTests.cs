using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveTranscodeTypeTests
{
    [Theory]
    [InlineData(LiveEncoder.Software)]
    [InlineData(LiveEncoder.Vaapi)]
    public void AnEncoderThatWasAskedForCarriesNoReasonToHaveFallenBack(LiveEncoder encoder)
    {
        LiveEncoderChoice chosen = LiveEncoderChoice.Asked(encoder);

        Assert.Equal(encoder, chosen.Encoder);
        Assert.False(chosen.FellBack);
        Assert.Null(chosen.FellBackBecause);
        Assert.Equal(string.Empty, chosen.Note);
    }

    [Fact]
    public void AnEncoderThatIsNotOneOfTheTwoCannotBeChosen()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveEncoderChoice.Asked((LiveEncoder)7));
    }

    [Theory]
    [InlineData(EncoderRefusal.NodeMissing)]
    [InlineData(EncoderRefusal.NodeUnreadable)]
    [InlineData(EncoderRefusal.DriverUnusable)]
    [InlineData(EncoderRefusal.ProbeTimedOut)]
    [InlineData(EncoderRefusal.ProbeProgrammeMissing)]
    public void FallingBackLandsOnSoftwareAndSaysWhy(EncoderRefusal because)
    {
        LiveEncoderChoice chosen = LiveEncoderChoice.FellBackToSoftware(because, "the card said no");

        Assert.Equal(LiveEncoder.Software, chosen.Encoder);
        Assert.True(chosen.FellBack);
        Assert.Equal(because, chosen.FellBackBecause);
        Assert.Equal("the card said no", chosen.Note);
    }

    [Fact]
    public void FallingBackForNoNamedReasonIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LiveEncoderChoice.FellBackToSoftware((EncoderRefusal)9, "silently"));
    }

    [Fact]
    public void WhyItFellBackCarriesNoPathFromThisMachine()
    {
        LiveEncoderChoice chosen = LiveEncoderChoice.FellBackToSoftware(
            EncoderRefusal.DriverUnusable,
            "No VA display found for device /dev/dri/renderD128.");

        Assert.DoesNotContain('/', chosen.Note);
    }

    [Fact]
    public void TheOnlyReasonsACardIsTurnedDownAreThese()
    {
        Assert.Equal(
            [
                EncoderRefusal.NodeMissing,
                EncoderRefusal.NodeUnreadable,
                EncoderRefusal.DriverUnusable,
                EncoderRefusal.ProbeTimedOut,
                EncoderRefusal.ProbeProgrammeMissing,
            ],
            Enum.GetValues<EncoderRefusal>());
    }

    [Fact]
    public void APictureIsEncodedInSoftwareUntilSomebodyAsksForTheCard()
    {
        Assert.Equal(LiveEncoder.Software, new LiveTranscodeSettings().Prefer);
        Assert.Equal("ffmpeg", new LiveTranscodeSettings().Programme);
    }

    [Fact]
    public void AProgrammeThatExitedZeroRanToTheEnd()
    {
        TranscoderExit ended = TranscoderExit.Finished();

        Assert.True(ended.RanToTheEnd);
        Assert.Null(ended.Fault);
        Assert.Equal(0, ended.ExitCode);
    }

    [Fact]
    public void AProgrammeThatRefusedSaysWithWhatCodeAndWhatItComplainedOf()
    {
        TranscoderExit refused = TranscoderExit.Refused(1, "Invalid data found when processing input");

        Assert.False(refused.RanToTheEnd);
        Assert.Equal(TranscoderFault.Refused, refused.Fault);
        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("Invalid data", refused.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void AProgrammeThatExitedZeroWasNotRefusedByIt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TranscoderExit.Refused(0, "fine"));
    }

    [Fact]
    public void AProgrammeThatWasCalledOffHasNoCodeToReport()
    {
        TranscoderExit off = TranscoderExit.CalledOff("nothing said");

        Assert.False(off.RanToTheEnd);
        Assert.Equal(TranscoderFault.CalledOff, off.Fault);
        Assert.Null(off.ExitCode);
    }

    [Fact]
    public void AStartThatFailedCarriesNeitherATranscoderNorAPath()
    {
        LiveTranscoderStart failed = LiveTranscoderStart.Failed(
            TranscoderFault.ProgrammeMissing,
            "'/usr/local/bin/ffmpeg' could not be started on this machine");

        Assert.False(failed.Running);
        Assert.Null(failed.Transcoder);
        Assert.Equal(TranscoderFault.ProgrammeMissing, failed.Fault);
        Assert.DoesNotContain('/', failed.Note);
    }

    [Fact]
    public void AStartThatFailedForNoNamedReasonIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LiveTranscoderStart.Failed((TranscoderFault)9, "silently"));
    }
}
