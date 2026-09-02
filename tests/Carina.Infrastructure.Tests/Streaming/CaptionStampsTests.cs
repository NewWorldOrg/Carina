using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class CaptionStampsTests
{
    private const string Clock = "[Parsed_showinfo_1 @ 0x55a70aad5200] config in time_base: 1/90000, frame_rate: 0/1";

    private const string First =
        "[Parsed_showinfo_1 @ 0x55a70aad5200] n:   0 pts:6908746875 pts_time:76763.9 duration:      0 duration_time:0       fmt:bgra cl:unspecified sar:0/1 s:1440x1080 i:P iskey:0 type:? checksum:03DD7670 plane_checksum:[03DD7670] mean:[2] stdev:[19.8]";

    private const string Twelfth =
        "[Parsed_showinfo_1 @ 0x55a70aad5200] n:  12 pts:6908955585 pts_time:76766.2 duration:      0 duration_time:0       fmt:bgra cl:unspecified sar:0/1 s:1440x1080 i:P iskey:0 type:? checksum:F95D8C63 plane_checksum:[F95D8C63] mean:[4] stdev:[25.5]";

    [Fact]
    public void AStampIsTheFrameNumberAndThePtsInTicksOfTheNinetyKilohertzClock()
    {
        CaptionStamps stamps = new();

        Assert.Equal(new CaptionStamp(0, LivePts.Of(6_908_746_875UL)), stamps.Read(First));
        Assert.Equal(new CaptionStamp(12, LivePts.Of(6_908_955_585UL)), stamps.Read(Twelfth));
    }

    [Fact]
    public void ThePtsIsReadAsTicksNotFromTheRoundedSecondsBesideIt()
    {
        CaptionStamp? stamp = new CaptionStamps().Read(First);

        Assert.NotNull(stamp);
        Assert.NotEqual(76_763.9 * LivePts.Hertz, stamp.Pts!.Value);
        Assert.Equal(6_908_746_875UL, stamp.Pts.Value);
    }

    [Fact]
    public void TheClockLineIsNotAStampButSaysHowToReadTheOnesAfterIt()
    {
        CaptionStamps stamps = new();

        Assert.Null(stamps.Read(Clock));
        Assert.Null(stamps.Read("[Parsed_showinfo_1 @ 0x55a70aad5200] config out time_base: 0/0, frame_rate: 0/0"));
        Assert.Equal(LivePts.Of(6_908_746_875UL), stamps.Read(First)!.Pts);
    }

    [Fact]
    public void AStampOnAnotherClockIsRescaledToNinetyKilohertz()
    {
        CaptionStamps stamps = new();

        stamps.Read("[Parsed_showinfo_1 @ 0x1] config in time_base: 1/1000, frame_rate: 0/1");

        Assert.Equal(LivePts.Of(90_000UL), stamps.Read("[Parsed_showinfo_1 @ 0x1] n:   0 pts:1000 pts_time:1 duration: 0 ")!.Pts);

        stamps.Read("[Parsed_showinfo_1 @ 0x1] config in time_base: 1001/30000, frame_rate: 30000/1001");

        Assert.Equal(LivePts.Of(3_003UL), stamps.Read("[Parsed_showinfo_1 @ 0x1] n:   1 pts:1 pts_time:0.03 duration: 0 ")!.Pts);
    }

    [Fact]
    public void AClockThatTicksNoTimesASecondIsIgnored()
    {
        CaptionStamps stamps = new();

        stamps.Read("[Parsed_showinfo_1 @ 0x1] config in time_base: 0/0, frame_rate: 0/0");

        Assert.Equal(LivePts.Of(90_000UL), stamps.Read("[Parsed_showinfo_1 @ 0x1] n:   0 pts:90000 pts_time:1 duration: 0 ")!.Pts);
    }

    [Fact]
    public void AFrameWithoutAReadablePtsIsStillCountedButCarriesNoTime()
    {
        CaptionStamp? stamp = new CaptionStamps().Read("[Parsed_showinfo_1 @ 0x1] n:   3 pts:NOPTS pts_time:NOPTS duration: 0 ");

        Assert.Equal(new CaptionStamp(3, null), stamp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Input #0, mpegts, from 'pipe:0':")]
    [InlineData("[mpegts @ 0x55ccb07d7940] PES packet size mismatch")]
    [InlineData("[mpeg2video @ 0x556dabd05000] Invalid frame dimensions 0x0.")]
    [InlineData("sub2video: non-bitmap subtitle")]
    [InlineData("frame=  113 fps=0.0 q=-0.0 Lsize=  686475kB time=00:00:00.00 bitrate=N/A speed=   0x")]
    [InlineData("[Parsed_showinfo_1 @ 0x1] color_range:unknown color_space:unknown color_primaries:unknown color_trc:unknown")]
    public void AnythingElseTheProgrammeSaysIsNotAStamp(string line)
    {
        Assert.Null(new CaptionStamps().Read(line));
    }

    [Fact]
    public void ALineIsRequired()
    {
        Assert.Throws<ArgumentNullException>(() => new CaptionStamps().Read(null!));
    }
}
