using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class CaptionFontTests
{
    [Fact]
    public void TheCaptionsAreDrawnWithDenpaFontAndWhatItLacksFallsToNoto()
    {
        Assert.Equal(["Denpa Font", "Noto Sans CJK JP"], FfmpegLiveInvocation.Font.Split(','));
    }
}
