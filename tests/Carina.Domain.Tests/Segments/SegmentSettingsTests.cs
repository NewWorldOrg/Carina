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

    [Fact(DisplayName = "BR-CD-009: how likely a stretch is before a button offers to skip it stands at 0.70 while no setting holds another")]
    public void TheSkipThresholdStandsAtItsDefault()
    {
        Assert.Equal(0.70, SegmentSettingsStanding.Unset.SkipThreshold);
        Assert.Null(SegmentSettingsStanding.Unset.SkipThresholdChangedAt);
        Assert.Equal(0.70, SegmentSettingsStanding.Over(SegmentSettings.LearningSwitched(true, Noon)).SkipThreshold);
        Assert.Null(SegmentSettingsStanding.Over(SegmentSettings.LearningSwitched(true, Noon)).SkipThresholdChangedAt);
    }

    [Fact(DisplayName = "BR-CV-002: a row held stands at the threshold it keeps, with when it was changed")]
    public void ARowHeldStandsAtTheThresholdItKeeps()
    {
        SegmentSettingsStanding standing = SegmentSettingsStanding.Over(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, false, null, 0.65, Noon));

        Assert.False(standing.Learning);
        Assert.Null(standing.LearningChangedAt);
        Assert.Equal(0.65, standing.SkipThreshold);
        Assert.Equal(Noon, standing.SkipThresholdChangedAt);
    }

    [Theory(DisplayName = "BR-CV-002: a row keeps a threshold from 0.50 to 0.95 only, timed in UTC")]
    [InlineData(0.49)]
    [InlineData(0.96)]
    [InlineData(double.NaN)]
    public void ARowKeepsAThresholdFromHalfToNineteenTwentieths(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, false, null, threshold, Noon));
        Assert.Throws<ArgumentException>(
            () => SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, false, null, 0.7, DateTime.SpecifyKind(Noon, DateTimeKind.Local)));
    }

    [Theory(DisplayName = "BR-CV-002: a threshold asked for from 0.50 to 0.95 is taken to the hundredth, and one outside is not taken")]
    [InlineData(0.5, 0.5)]
    [InlineData(0.95, 0.95)]
    [InlineData(0.654, 0.65)]
    [InlineData(0.655, 0.66)]
    [InlineData(0.7049999, 0.7)]
    [InlineData(0.4999, null)]
    [InlineData(0.9501, null)]
    [InlineData(-1, null)]
    [InlineData(double.NaN, null)]
    [InlineData(double.PositiveInfinity, null)]
    public void AThresholdAskedForIsTakenToTheHundredth(double asked, double? taken)
        => Assert.Equal(taken, SegmentSettingsStanding.SkipThresholdOf(asked));

    [Fact(DisplayName = "BR-CV-002: setting the threshold on no row keeps learning off and unswitched, and says when the threshold changed")]
    public void SettingTheThresholdOnNoRowKeepsLearningOff()
    {
        SegmentSettings? set = SegmentSettingsStanding.Unset.Set(null, 0.65, Noon);

        Assert.NotNull(set);
        Assert.Equal(SegmentSettings.TheOnlyRow, set.Id);
        Assert.False(set.Learning);
        Assert.Null(set.LearningChangedAt);
        Assert.Equal(0.65, set.SkipThreshold);
        Assert.Equal(Noon, set.SkipThresholdChangedAt);
    }

    [Fact(DisplayName = "BR-CV-002: only a setting whose value moves takes the time of the change; the other keeps its own")]
    public void OnlyASettingWhoseValueMovesTakesTheTime()
    {
        SegmentSettingsStanding standing = SegmentSettingsStanding.Over(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, true, Noon, 0.65, Noon.AddMinutes(1)));
        DateTime later = Noon.AddHours(1);

        SegmentSettings? threshold = standing.Set(true, 0.8, later);
        SegmentSettings? learning = standing.Set(false, 0.65, later);
        SegmentSettings? both = standing.Set(false, 0.8, later);

        Assert.Equal((true, Noon, 0.8, later), (threshold!.Learning, threshold.LearningChangedAt, threshold.SkipThreshold, threshold.SkipThresholdChangedAt));
        Assert.Equal((false, later, 0.65, Noon.AddMinutes(1)), (learning!.Learning, learning.LearningChangedAt, learning.SkipThreshold, learning.SkipThresholdChangedAt));
        Assert.Equal((false, later, 0.8, later), (both!.Learning, both.LearningChangedAt, both.SkipThreshold, both.SkipThresholdChangedAt));
    }

    [Fact(DisplayName = "BR-CV-002: a change that moves no value, or names nothing, writes no row")]
    public void AChangeThatMovesNoValueWritesNoRow()
    {
        SegmentSettingsStanding standing = SegmentSettingsStanding.Over(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, true, Noon, 0.65, Noon));

        Assert.Null(standing.Set(true, 0.65, Noon.AddHours(1)));
        Assert.Null(standing.Set(null, null, Noon.AddHours(1)));
        Assert.Null(SegmentSettingsStanding.Unset.Set(false, 0.7, Noon));
    }

    [Fact(DisplayName = "a change sets only a threshold it would take")]
    public void AChangeSetsOnlyAThresholdItWouldTake()
        => Assert.Throws<ArgumentOutOfRangeException>(() => SegmentSettingsStanding.Unset.Set(null, 0.654, Noon));
}
