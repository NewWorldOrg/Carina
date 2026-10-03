using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class FfmpegCaptionInvocationTests
{
    private static readonly ServiceId Service = new(1040);

    private static readonly ServiceId AnotherService = new(1048);

    private static readonly StreamAttributes Interlaced = new(
        new VideoSize(1440, 1080),
        ScanType.Interlaced,
        FrameRate.BroadcastFrames,
        AudioMode.Stereo);

    [Fact]
    public void BrPd016TheCaptionsOfARecordedFileAreDrawnTheWayTheyAreDrawnBesideThePictureWithNoPictureDecoded()
    {
        string[] alone = [.. FfmpegCaptionInvocation.Arguments(Service, Interlaced, new StreamSource("/srv/recordings/k-1.ts"))];
        string[] beside = [.. FfmpegLiveInvocation.CaptionDelivery(Service, 35)];
        int input = Array.IndexOf(alone, "-i");

        Assert.Equal(["-sub_type", "bitmap", "-canvas_size", "1440x1080", "-font", FfmpegLiveInvocation.Font], alone[(input - 6)..input]);
        Assert.Equal("/srv/recordings/k-1.ts", alone[input + 1]);
        string[] carried = [.. FfmpegCaptionInvocation.Carried(Service)];
        int drawn = Array.IndexOf(alone, "-filter_complex");

        string[] withoutWhatIsCarried = [.. alone[drawn..(drawn + 4)], .. alone[(drawn + 4 + carried.Length)..^1]];

        Assert.Equal(beside[..^1], withoutWhatIsCarried);
        Assert.Equal(carried, alone[(drawn + 4)..(drawn + 4 + carried.Length)]);
        Assert.Equal("pipe:1", alone[^1]);
        Assert.Contains("-copyts", alone[..input]);
        Assert.DoesNotContain("-map", alone[..drawn]);
        Assert.DoesNotContain(alone, argument => argument.Contains(":v:", StringComparison.Ordinal));
    }

    [Fact]
    public void BrPd019TheCaptionStreamIsCarriedAsItIsBesideThePicturesSoItsTextCanBeRead()
    {
        Assert.Equal(
            ["-map", "0:p:1040:s:0", "-c:s", "copy", "-tag:s", FfmpegCaptionInvocation.CarriedTag],
            FfmpegCaptionInvocation.Carried(Service));
    }

    [Fact]
    public void BrPd016ARecordedFilesClockIsLiftedByWholeSecondsBeyondWhatThirtyThreeBitsHold()
    {
        string[] alone = [.. FfmpegCaptionInvocation.Arguments(Service, Interlaced, new StreamSource("/srv/recordings/k-1.ts"))];

        Assert.Equal("100000", alone[Array.IndexOf(alone, "-output_ts_offset") + 1]);
        Assert.True(Array.IndexOf(alone, "-output_ts_offset") > Array.IndexOf(alone, "-i"));
        Assert.True((long)FfmpegCaptionInvocation.ClockLiftedBySeconds * LivePts.Hertz > (long)LivePts.ComesAroundAt);
    }

    [Fact]
    public void TheCaptionsOfARecordedFileNameNoOtherFileAndNoOtherService()
    {
        Assert.NotEqual(
            FfmpegCaptionInvocation.Arguments(Service, Interlaced, new StreamSource("/srv/recordings/k-1.ts")),
            FfmpegCaptionInvocation.Arguments(AnotherService, Interlaced, new StreamSource("/srv/recordings/k-1.ts")));
        Assert.Throws<ArgumentNullException>(() => FfmpegCaptionInvocation.Arguments(Service, Interlaced, null!));
    }
}
