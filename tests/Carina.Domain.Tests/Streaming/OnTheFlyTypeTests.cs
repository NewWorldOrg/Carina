using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class OnTheFlyTypeTests
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

    [Fact]
    public void ARefusalNamesNoPathOnThisMachine()
    {
        OnTheFlyStart refused = OnTheFlyStart.Refused(
            OnTheFlyRefusal.NothingCameOut,
            "Error opening input /srv/recordings/a1b2c3.ts: No such file");

        Assert.False(refused.Running);
        Assert.Null(refused.Viewing);
        Assert.Equal(OnTheFlyRefusal.NothingCameOut, refused.Refusal);
        Assert.DoesNotContain('/', refused.Note);
        Assert.Contains("Error opening input", refused.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordingIsRefusedForOneOfTheReasonsNamedRatherThanAnyNumberAtAll()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OnTheFlyStart.Refused((OnTheFlyRefusal)99, "no"));
        Assert.Throws<ArgumentNullException>(() => OnTheFlyStart.Started(null!));
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
