using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityThresholdHistoryTests
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WithNoChangeTheLevelIsTheOneStandingNow()
    {
        QualityThresholdHistory history = QualityThresholdHistory.Of(Standing(0.0004), []);

        Assert.Equal(0.0004, Level(history.At(Noon.AddDays(-30))));
    }

    [Fact(DisplayName = "BR-QV-002: a moment before a change is judged against the level the change replaced")]
    public void AMomentBeforeAChangeIsJudgedAgainstTheLevelTheChangeReplaced()
    {
        QualityThresholdHistory history = QualityThresholdHistory.Of(
            Standing(0.0004),
            [Change(0.0003, 0.0004, Noon.AddDays(-1)), Change(0.0002, 0.0003, Noon.AddDays(-2))]);

        Assert.Equal(0.0002, Level(history.At(Noon.AddDays(-3))));
        Assert.Equal(0.0003, Level(history.At(Noon.AddDays(-1).AddHours(-1))));
        Assert.Equal(0.0004, Level(history.At(Noon)));
    }

    [Fact]
    public void AChangeMadeTheMomentABucketClosesBelongsToTheBucketAfter()
    {
        QualityThresholdHistory history = QualityThresholdHistory.Of(Standing(0.0004), [Change(0.0002, 0.0004, Noon)]);

        Assert.Equal(0.0002, Level(history.At(Noon)));
        Assert.Equal(0.0004, Level(history.At(Noon.AddTicks(1))));
    }

    [Fact]
    public void AChangeToAnotherLevelLeavesThisOneAlone()
    {
        QualityThresholdHistory history = QualityThresholdHistory.Of(
            Standing(0.0004),
            [QualityThresholdChange.Record(QualityThresholdChangeId.New(), QualityThresholdKey.Overflows, 1, 5, Noon, null)]);

        Assert.Equal(0.0004, Level(history.At(Noon.AddDays(-1))));
    }

    private static IReadOnlyList<QualityThresholdStanding> Standing(double warning)
        =>
        [
            .. QualityThresholdStanding.Over([], Noon).Select(standing => standing.Key is QualityThresholdKey.PacketsLostWarning
                ? standing with { Setting = QualityFactory.Moved(0.0002, warning) }
                : standing),
        ];

    private static QualityThresholdChange Change(double previous, double next, DateTime at)
        => QualityThresholdChange.Record(
            QualityThresholdChangeId.New(),
            QualityThresholdKey.PacketsLostWarning,
            previous,
            next,
            at,
            null);

    private static double Level(IReadOnlyList<QualityThresholdStanding> standings)
        => standings.First(standing => standing.Key == QualityThresholdKey.PacketsLostWarning).Setting.Current;
}
