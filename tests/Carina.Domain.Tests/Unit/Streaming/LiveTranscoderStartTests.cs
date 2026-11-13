using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class LiveTranscoderStartTests
{
    [Fact]
    public void AStartThatFailedCarriesNeitherATranscoderNorAPath()
    {
        LiveTranscoderStart failed = LiveTranscoderStart.Failed(
            TranscoderFault.ProgrammeMissing,
            "'/usr/local/bin/ffmpeg' could not be started on this machine");

        Assert.False(failed.Running);
        Assert.Null(failed.Transcoder);
        Assert.Equal(TranscoderFault.ProgrammeMissing, failed.Fault);
        Assert.NotEmpty(failed.Note);
        Assert.DoesNotContain('/', failed.Note);
    }

    [Fact]
    public void AStartThatFailedForNoNamedReasonIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LiveTranscoderStart.Failed((TranscoderFault)9, "silently"));
    }

    [Fact]
    public void ALiveStartRefusedAtTheCeilingSaysSoInTypeAndInWords()
    {
        TranscodeCeiling ceiling = new(4, 4);

        LiveTranscoderStart refused = LiveTranscoderStart.Refused(ceiling);

        Assert.False(refused.Running);
        Assert.Null(refused.Transcoder);
        Assert.Equal(TranscoderFault.TooManyAlready, refused.Fault);
        Assert.Same(ceiling, refused.Ceiling);
        Assert.Equal(ceiling.Said, refused.Note);
    }

    [Fact]
    public void ALiveStartThatFailedAnotherWayCarriesNoCeiling()
    {
        LiveTranscoderStart failed = LiveTranscoderStart.Failed(TranscoderFault.ProgrammeMissing, "gone");

        Assert.Null(failed.Ceiling);
        Assert.Throws<ArgumentNullException>(() => LiveTranscoderStart.Refused(null!));
    }
}
