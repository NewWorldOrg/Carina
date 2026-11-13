using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class TranscodePurposeTests
{
    [Fact]
    public void ATranscoderIsRaisedForOneOfTheTwoPurposesThereAre()
    {
        Assert.Equal(
            [TranscodePurpose.Live, TranscodePurpose.Playback],
            Enum.GetValues<TranscodePurpose>().Order().ToArray());
    }
}
