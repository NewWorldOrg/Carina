using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class TranscoderNoteTests
{
    [Theory]
    [InlineData("Failed to open /dev/dri/renderD128: Permission denied")]
    [InlineData("libva: Trying to open /usr/lib/x86_64-linux-gnu/dri/iHD_drv_video.so")]
    [InlineData("'/usr/local/bin/ffmpeg' could not be started on this machine")]
    [InlineData("[out#0 @ 0x1] Error opening output /srv/recordings/k-1.ts")]
    public void APathOnThisMachineIsNotPartOfWhatAViewerIsTold(string said)
    {
        string kept = TranscoderNote.Of(said);

        Assert.DoesNotContain('/', kept);
        Assert.Contains(TranscoderNote.InsteadOfAPath, kept, StringComparison.Ordinal);
    }

    [Fact]
    public void WhyItFailedSurvivesTheHiding()
    {
        Assert.Contains(
            "Permission denied",
            TranscoderNote.Of("Failed to open /dev/dri/renderD128: Permission denied"),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("30000/1001")]
    [InlineData("h264_vaapi @ 0x5587930fce80")]
    [InlineData("bwdif=mode=send_field,scale=1280:720")]
    public void ASlashInTheMiddleOfSomethingIsNotAPath(string said)
    {
        Assert.Equal(said, TranscoderNote.Of(said));
    }

    [Fact]
    public void TheTailIsWhatIsKeptBecauseThatIsWhereTheFailureIs()
    {
        string said = new string('a', TranscoderNote.Longest) + "the last thing said";

        string kept = TranscoderNote.Of(said);

        Assert.Equal(TranscoderNote.Longest, kept.Length);
        Assert.EndsWith("the last thing said", kept, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingSaidIsNothingKept()
    {
        Assert.Equal(string.Empty, TranscoderNote.Of("   \n  "));
    }
}
