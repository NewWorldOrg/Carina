using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class LiveTranscodeSettingsTests
{
    [Fact]
    public void APictureIsEncodedInSoftwareUntilSomebodyAsksForTheCard()
    {
        Assert.Equal(LiveEncoder.Software, new LiveTranscodeSettings().Prefer);
        Assert.Equal("ffmpeg", new LiveTranscodeSettings().Programme);
    }
}
