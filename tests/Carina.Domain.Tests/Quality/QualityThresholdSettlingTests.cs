using Carina.Domain.Auth;
using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityThresholdSettlingTests
{
    private static readonly DateTime Earlier = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime At = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private const string Someone = "someone@example.org";

    private const string SomeoneElse = "someone-else";

    private static readonly QualityThresholdMeasurement Measurement =
        QualityThresholdMeasurement.Of(19_500, 240, 18, Earlier.AddDays(-7), Earlier, Earlier);

    [Fact(DisplayName = "BR-QD-023: a level measured over the shipped one is taken, stops being provisional, and the change says it was measured")]
    public void ALevelMeasuredOverTheShippedOneIsTaken()
    {
        QualityThresholdSettled settled = QualityThresholdSettling.Measured(Shipped(), Measurement);

        Assert.Equal(19_500, settled.Threshold.Setting.Current);
        Assert.Equal(15_000, settled.Threshold.Setting.Default);
        Assert.False(settled.Threshold.Setting.Provisional);
        Assert.Equal(240, settled.Threshold.Setting.Observations);
        Assert.Equal(Measurement.MeasuredAt, settled.Threshold.Setting.UpdatedAt);
        Assert.False(settled.Threshold.ByHand);
        Assert.Equal(Measurement, settled.Threshold.Measurement);

        QualityThresholdChange change = Assert.IsType<QualityThresholdChange>(settled.Change);

        Assert.Equal(15_000, change.PreviousValue);
        Assert.Equal(19_500, change.NextValue);
        Assert.Equal(QualityThresholdChangeCause.Measurement, change.Cause);
        Assert.Equal(Measurement.MeasuredAt, change.ChangedAt);
    }

    [Fact(DisplayName = "BR-QD-023: a measurement that lands on the value in force keeps its new grounds and records no change")]
    public void AMeasurementThatLandsOnTheValueInForceRecordsNoChange()
    {
        QualityThresholdStanding measured = Standing(QualityThresholdSettling.Measured(Shipped(), Measurement).Threshold);
        QualityThresholdMeasurement again = QualityThresholdMeasurement.Of(19_500, 300, 20, At.AddDays(-7), At, At);

        QualityThresholdSettled settled = QualityThresholdSettling.Measured(measured, again);

        Assert.Null(settled.Change);
        Assert.Equal(again, settled.Threshold.Measurement);
        Assert.Equal(300, settled.Threshold.Setting.Observations);
    }

    [Fact(DisplayName = "BR-QD-023: a level set by hand is not moved by a measurement, which is still kept beside it")]
    public void ALevelSetByHandIsNotMovedByAMeasurement()
    {
        QualityThresholdStanding byHand = Standing(QualityThresholdSettling.ByHand(Shipped(), 12_000, Earlier, Someone).Threshold);

        QualityThresholdSettled settled = QualityThresholdSettling.Measured(byHand, Measurement);

        Assert.Null(settled.Change);
        Assert.Equal(12_000, settled.Threshold.Setting.Current);
        Assert.True(settled.Threshold.ByHand);
        Assert.True(settled.Threshold.Setting.Provisional);
        Assert.Equal(Measurement, settled.Threshold.Measurement);
    }

    [Fact(DisplayName = "BR-QD-023: setting a level by hand keeps the measurement, is provisional, and the change says it was by hand")]
    public void SettingALevelByHandKeepsTheMeasurement()
    {
        QualityThresholdStanding measured = Standing(QualityThresholdSettling.Measured(Shipped(), Measurement).Threshold);

        QualityThresholdSettled settled = QualityThresholdSettling.ByHand(measured, 20_000, At, Someone);

        Assert.Equal(20_000, settled.Threshold.Setting.Current);
        Assert.True(settled.Threshold.ByHand);
        Assert.True(settled.Threshold.Setting.Provisional);
        Assert.Equal(0, settled.Threshold.Setting.Observations);
        Assert.Equal(Measurement, settled.Threshold.Measurement);

        QualityThresholdChange change = Assert.IsType<QualityThresholdChange>(settled.Change);

        Assert.Equal(19_500, change.PreviousValue);
        Assert.Equal(20_000, change.NextValue);
        Assert.Equal(QualityThresholdChangeCause.Hand, change.Cause);
        Assert.Equal(At, change.ChangedAt);
    }

    [Fact(DisplayName = "BR-QD-023: letting go of a level set by hand returns it to the measurement")]
    public void LettingGoOfALevelSetByHandReturnsItToTheMeasurement()
    {
        QualityThresholdStanding measured = Standing(QualityThresholdSettling.Measured(Shipped(), Measurement).Threshold);
        QualityThresholdStanding byHand = Standing(QualityThresholdSettling.ByHand(measured, 20_000, Earlier, Someone).Threshold);

        QualityThresholdSettled settled = QualityThresholdSettling.Released(byHand, At, Someone);

        Assert.Equal(19_500, settled.Threshold.Setting.Current);
        Assert.False(settled.Threshold.ByHand);
        Assert.False(settled.Threshold.Setting.Provisional);
        Assert.Equal(240, settled.Threshold.Setting.Observations);

        QualityThresholdChange change = Assert.IsType<QualityThresholdChange>(settled.Change);

        Assert.Equal(20_000, change.PreviousValue);
        Assert.Equal(19_500, change.NextValue);
        Assert.Equal(QualityThresholdChangeCause.Hand, change.Cause);
    }

    [Fact(DisplayName = "BR-QD-023: letting go of a level set by hand with nothing measured returns it to the shipped one")]
    public void LettingGoWithNothingMeasuredReturnsItToTheShippedOne()
    {
        QualityThresholdStanding byHand = Standing(QualityThresholdSettling.ByHand(Shipped(), 20_000, Earlier, Someone).Threshold);

        QualityThresholdSettled settled = QualityThresholdSettling.Released(byHand, At, Someone);

        Assert.Equal(15_000, settled.Threshold.Setting.Current);
        Assert.False(settled.Threshold.ByHand);
        Assert.True(settled.Threshold.Setting.Provisional);
        Assert.Null(settled.Threshold.Measurement);
        Assert.Equal(20_000, settled.Change!.PreviousValue);
        Assert.Equal(15_000, settled.Change.NextValue);
    }

    [Fact(DisplayName = "BR-QD-023: letting go of a level nobody set by hand changes nothing")]
    public void LettingGoOfALevelNobodySetByHandChangesNothing()
    {
        QualityThresholdSettled settled = QualityThresholdSettling.Released(Shipped(), At, Someone);

        Assert.Null(settled.Change);
        Assert.Equal(15_000, settled.Threshold.Setting.Current);
        Assert.False(settled.Threshold.ByHand);
    }

    [Fact(DisplayName = "BR-QV-002: a level set by hand names who set it, on the level and on the change")]
    public void ALevelSetByHandNamesWhoSetIt()
    {
        QualityThresholdSettled settled = QualityThresholdSettling.ByHand(Shipped(), 12_000, At, Someone);

        Assert.Equal(Someone, settled.Threshold.UpdatedBy);
        Assert.Equal(Someone, Assert.IsType<QualityThresholdChange>(settled.Change).ChangedBy);
    }

    [Fact(DisplayName = "BR-QV-002: letting go of a level set by hand names who let go of it")]
    public void LettingGoOfALevelSetByHandNamesWhoLetGoOfIt()
    {
        QualityThresholdStanding byHand = Standing(QualityThresholdSettling.ByHand(Shipped(), 12_000, Earlier, Someone).Threshold);

        QualityThresholdSettled settled = QualityThresholdSettling.Released(byHand, At, SomeoneElse);

        Assert.Equal(SomeoneElse, settled.Threshold.UpdatedBy);
        Assert.Equal(SomeoneElse, Assert.IsType<QualityThresholdChange>(settled.Change).ChangedBy);
    }

    [Fact(DisplayName = "BR-QV-002: a level a measurement decides names nobody, on the level or on the change")]
    public void ALevelAMeasurementDecidesNamesNobody()
    {
        QualityThresholdStanding released = Standing(QualityThresholdSettling.Released(
            Standing(QualityThresholdSettling.ByHand(Shipped(), 12_000, Earlier, Someone).Threshold),
            Earlier,
            SomeoneElse).Threshold);

        QualityThresholdSettled settled = QualityThresholdSettling.Measured(released, Measurement);

        Assert.Null(settled.Threshold.UpdatedBy);
        Assert.Null(Assert.IsType<QualityThresholdChange>(settled.Change).ChangedBy);
    }

    [Fact(DisplayName = "BR-QV-002: a measurement kept beside a level set by hand leaves who set it as it was")]
    public void AMeasurementKeptBesideALevelSetByHandLeavesWhoSetIt()
    {
        QualityThresholdStanding byHand = Standing(QualityThresholdSettling.ByHand(Shipped(), 12_000, Earlier, Someone).Threshold);

        QualityThresholdSettled settled = QualityThresholdSettling.Measured(byHand, Measurement);

        Assert.Equal(Someone, settled.Threshold.UpdatedBy);
    }

    [Fact(DisplayName = "BR-QV-002: the longest display name a session can carry is kept whole as who changed a level")]
    public void TheLongestDisplayNameASessionCanCarryIsKeptWhole()
    {
        string longest = new('a', AuthSession.LongestDisplayName);

        QualityThresholdSettled settled = QualityThresholdSettling.ByHand(Shipped(), 12_000, At, longest);

        Assert.Equal(longest, settled.Threshold.UpdatedBy);
        Assert.Equal(longest, settled.Change!.ChangedBy);
    }

    private static QualityThresholdStanding Shipped()
        => QualityThresholdStanding.Over([], Earlier).First(standing => standing.Key == QualityThresholdKey.CarrierToNoiseFloor);

    private static QualityThresholdStanding Standing(QualityThreshold threshold)
        => QualityThresholdStanding.Over([threshold], At).First(standing => standing.Key == threshold.Key);
}
