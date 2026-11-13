using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class OnTheFlyStandingTests
{
    [Fact]
    public void AStandingSaysWhereInTheRecordingItsFirstByteSitsAndWhatGettingThereCost()
    {
        var standing = new OnTheFlyStanding(
            TimeSpan.FromMinutes(12),
            TimeSpan.FromMilliseconds(640),
            LiveProfile.Hd30,
            LiveEncoderChoice.Asked(LiveEncoder.Software),
            attributesWereMeasured: true,
            running: 1,
            atOnce: 2);

        Assert.Equal(TimeSpan.FromMinutes(12), standing.StartsAt);
        Assert.Equal(TimeSpan.FromMilliseconds(640), standing.Waited);
        Assert.Equal(LiveProfile.Hd30, standing.Profile);
        Assert.True(standing.AttributesWereMeasured);
        Assert.Equal(1, standing.Running);
        Assert.Equal(2, standing.AtOnce);
    }

    [Fact]
    public void AStandingHoldsNoNumberThatCouldNotHaveHappened()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Standing(startsAt: TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Standing(waited: TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Standing(running: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Standing(running: 3, atOnce: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Standing(atOnce: 0));
    }

    [Fact]
    public void AStandingIsToldWhichProfileAndWhichEncoderRatherThanNeither()
    {
        Assert.Throws<ArgumentNullException>(() => new OnTheFlyStanding(
            TimeSpan.Zero,
            TimeSpan.Zero,
            null!,
            LiveEncoderChoice.Asked(LiveEncoder.Software),
            attributesWereMeasured: false,
            running: 1,
            atOnce: 2));
        Assert.Throws<ArgumentNullException>(() => new OnTheFlyStanding(
            TimeSpan.Zero,
            TimeSpan.Zero,
            LiveProfile.Hd30,
            null!,
            attributesWereMeasured: false,
            running: 1,
            atOnce: 2));
    }

    private static OnTheFlyStanding Standing(
        TimeSpan? startsAt = null,
        TimeSpan? waited = null,
        int running = 1,
        int atOnce = 2)
        => new(
            startsAt ?? TimeSpan.Zero,
            waited ?? TimeSpan.Zero,
            LiveProfile.Hd30,
            LiveEncoderChoice.Asked(LiveEncoder.Software),
            attributesWereMeasured: false,
            running,
            atOnce);
}
