using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityThresholdTests
{
    private static readonly DateTime Declared = new(2026, 8, 21, 3, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-QD-003: every threshold this domain judges by is kept under a key it names")]
    public void EveryThresholdThisDomainJudgesByIsKeptUnderAKeyItNames()
    {
        QualityThreshold threshold = QualityThreshold.Declare(
            QualityThresholdKey.PacketsLostWarning,
            Threshold.Provisionally(0.0002, observations: 0, Declared));

        Assert.Equal(QualityThresholdKey.PacketsLostWarning, threshold.Key);
        Assert.True(threshold.Setting.Provisional);
        Assert.Null(threshold.UpdatedBy);
    }

    [Fact]
    public void AThresholdSomebodyMovedSaysWhoMovedIt()
    {
        QualityThreshold threshold = QualityThreshold.Rehydrate(
            QualityThresholdKey.SupplySilence,
            Threshold.Of(600, 900, provisional: true, observations: 4, Declared),
            "operator");

        Assert.Equal("operator", threshold.UpdatedBy);
        Assert.Equal(900, threshold.Setting.Current);
        Assert.Equal(600, threshold.Setting.Default);
    }

    [Fact]
    public void AKeyThisDomainDoesNotNameIsNoKey()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityThreshold.Declare(
            (QualityThresholdKey)99,
            Threshold.Provisionally(1, observations: 0, Declared)));
}
