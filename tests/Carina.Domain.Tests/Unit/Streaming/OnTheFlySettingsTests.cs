using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class OnTheFlySettingsTests
{
    [Fact]
    public void ATranscoderIsGivenHalfAMinuteForItsFirstByteUnlessItIsToldOtherwise()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), new OnTheFlySettings().LongestWaitForTheFirstByte);
    }

    [Fact]
    public void ATranscoderIsGivenSomeTimeToProduceItsFirstByteRatherThanNone()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OnTheFlySettings { LongestWaitForTheFirstByte = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OnTheFlySettings { LongestWaitForTheFirstByte = TimeSpan.FromSeconds(-1) });
    }
}
