using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentSettingsTests
{
    private static readonly DateTime Noon = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "switching learning is the one row, and says when it changed")]
    public void SwitchingLearningIsTheOneRow()
    {
        SegmentSettings switched = SegmentSettings.LearningSwitched(true, Noon);

        Assert.Equal(SegmentSettings.TheOnlyRow, switched.Id);
        Assert.True(switched.Learning);
        Assert.Equal(Noon, switched.LearningChangedAt);
    }

    [Fact(DisplayName = "the row is timed in UTC")]
    public void TheRowIsTimedInUtc()
        => Assert.Throws<ArgumentException>(
            () => SegmentSettings.LearningSwitched(true, new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Unspecified)));

    [Fact(DisplayName = "with no row held learning is off, and nobody has changed it")]
    public void WithNoRowHeldLearningIsOff()
    {
        SegmentSettingsStanding standing = SegmentSettingsStanding.Over(null);

        Assert.False(standing.Learning);
        Assert.Null(standing.LearningChangedAt);
        Assert.Equal(SegmentSettingsStanding.Unset, standing);
    }

    [Theory(DisplayName = "a row held is what stands, with when learning changed")]
    [InlineData(true)]
    [InlineData(false)]
    public void ARowHeldIsWhatStands(bool learning)
    {
        SegmentSettingsStanding standing = SegmentSettingsStanding.Over(SegmentSettings.LearningSwitched(learning, Noon));

        Assert.Equal(learning, standing.Learning);
        Assert.Equal(Noon, standing.LearningChangedAt);
    }
}
