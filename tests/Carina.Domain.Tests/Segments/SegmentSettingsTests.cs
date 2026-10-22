using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentSettingsTests
{
    private static readonly DateTime Noon = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "with no row held the settings stand unset")]
    public void WithNoRowHeldTheSettingsStandUnset()
        => Assert.Equal(SegmentSettingsStanding.Unset, SegmentSettingsStanding.Over(null));

    [Fact(DisplayName = "BR-CD-009: how likely a stretch is before a button offers to skip it stands at 0.70 while no setting holds another")]
    public void TheSkipThresholdStandsAtItsDefault()
    {
        Assert.Equal(0.70, SegmentSettingsStanding.Unset.SkipThreshold);
        Assert.Null(SegmentSettingsStanding.Unset.SkipThresholdChangedAt);
        Assert.Equal(0.70, SegmentSettingsStanding.Over(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, 0.70, null)).SkipThreshold);
        Assert.Null(SegmentSettingsStanding.Over(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, 0.70, null)).SkipThresholdChangedAt);
    }

    [Fact(DisplayName = "BR-CV-002: a row held stands at the threshold it keeps, with when it was changed")]
    public void ARowHeldStandsAtTheThresholdItKeeps()
    {
        SegmentSettingsStanding standing = SegmentSettingsStanding.Over(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, 0.65, Noon));

        Assert.Equal(0.65, standing.SkipThreshold);
        Assert.Equal(Noon, standing.SkipThresholdChangedAt);
    }

    [Theory(DisplayName = "BR-CV-002: a row keeps a threshold from 0.50 to 0.95 only, timed in UTC")]
    [InlineData(0.49)]
    [InlineData(0.96)]
    [InlineData(double.NaN)]
    public void ARowKeepsAThresholdFromHalfToNineteenTwentieths(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, threshold, Noon));
        Assert.Throws<ArgumentException>(
            () => SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, 0.7, DateTime.SpecifyKind(Noon, DateTimeKind.Local)));
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

    [Fact(DisplayName = "BR-CV-002: setting the threshold on no row makes the one row, and says when the threshold changed")]
    public void SettingTheThresholdOnNoRowMakesTheOneRow()
    {
        SegmentSettings? set = SegmentSettingsStanding.Unset.Set(0.65, Noon);

        Assert.NotNull(set);
        Assert.Equal(SegmentSettings.TheOnlyRow, set.Id);
        Assert.Equal(0.65, set.SkipThreshold);
        Assert.Equal(Noon, set.SkipThresholdChangedAt);
    }

    [Fact(DisplayName = "BR-CV-002: a threshold that moves takes the time of the change")]
    public void AThresholdThatMovesTakesTheTime()
    {
        SegmentSettingsStanding standing = SegmentSettingsStanding.Over(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, 0.65, Noon));
        DateTime later = Noon.AddHours(1);

        SegmentSettings? moved = standing.Set(0.8, later);

        Assert.Equal((0.8, later), (moved!.SkipThreshold, moved.SkipThresholdChangedAt));
    }

    [Fact(DisplayName = "BR-CV-002: a change that moves no value writes no row")]
    public void AChangeThatMovesNoValueWritesNoRow()
    {
        SegmentSettingsStanding standing = SegmentSettingsStanding.Over(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, 0.65, Noon));

        Assert.Null(standing.Set(0.65, Noon.AddHours(1)));
        Assert.Null(SegmentSettingsStanding.Unset.Set(0.7, Noon));
    }

    [Fact(DisplayName = "a change sets only a threshold it would take")]
    public void AChangeSetsOnlyAThresholdItWouldTake()
        => Assert.Throws<ArgumentOutOfRangeException>(() => SegmentSettingsStanding.Unset.Set(0.654, Noon));
}
