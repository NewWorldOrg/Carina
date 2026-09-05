using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Streaming;

namespace Carina.Domain.Tests;

public sealed class ProgrammeNoteTests
{
    [Theory]
    [InlineData("Failed to open /dev/dri/renderD128: Permission denied")]
    [InlineData("libva: Trying to open /usr/lib/x86_64-linux-gnu/dri/iHD_drv_video.so")]
    [InlineData("'/usr/local/bin/ffmpeg' could not be started on this machine")]
    [InlineData("[out#0 @ 0x1] Error opening output /srv/recordings/k-1.ts")]
    public void APathOnThisMachineIsNotPartOfWhatIsKept(string said)
    {
        string kept = ProgrammeNote.Of(said, ProgrammeNote.Longest);

        Assert.DoesNotContain('/', kept);
        Assert.Contains(ProgrammeNote.InsteadOfAPath, kept, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("30000/1001")]
    [InlineData("h264_vaapi @ 0x5587930fce80")]
    [InlineData("bwdif=mode=send_field,scale=1280:720")]
    public void ASlashInTheMiddleOfSomethingIsNotAPath(string said)
        => Assert.Equal(said, ProgrammeNote.Of(said, ProgrammeNote.Longest));

    [Fact]
    public void TheTailIsWhatIsKeptBecauseThatIsWhereTheFailureIs()
    {
        string said = new string('a', 40) + "the last thing said";

        Assert.Equal(20, ProgrammeNote.Of(said, 20).Length);
        Assert.EndsWith("thing said", ProgrammeNote.Of(said, 20), StringComparison.Ordinal);
    }

    [Fact]
    public void NothingSaidIsNothingKept()
        => Assert.Equal(string.Empty, ProgrammeNote.Of("   \n  ", ProgrammeNote.Longest));

    [Fact]
    public void ANoteIsCutAfterThePathsAreTakenOutSoTheLengthIsWhatIsActuallyKept()
    {
        string said = "/srv/recordings/" + new string('a', 2000) + ".ts is gone";

        Assert.Equal($"{ProgrammeNote.InsteadOfAPath} is gone", ProgrammeNote.Of(said, 20));
    }

    [Fact(DisplayName = "BR-ED2-012: what an encode failure keeps names no path on this machine, as the live side already did")]
    public void WhatAnEncodeFailureKeepsNamesNoPathOnThisMachine()
    {
        const string said = "[out#0 @ 0x1] Error opening output /srv/encoded/k-1.mp4: No space left on device";

        Assert.DoesNotContain('/', EncodeNote.Of(said));
        Assert.Contains("No space left on device", EncodeNote.Of(said), StringComparison.Ordinal);
    }

    [Fact]
    public void TheLiveSideAndTheEncodeSideHideAPathTheSameWay()
    {
        const string said = "could not open /dev/dri/renderD128";

        Assert.Equal(TranscoderNote.Of(said), EncodeNote.Of(said));
        Assert.Equal(ProgrammeNote.Of(said, ProgrammeNote.Longest), EncodeNote.Of(said));
    }
}
