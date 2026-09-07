using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityStandingTests
{
    [Fact(DisplayName = "BR-QS-001: one subject has six answers and the three grades sit inside them")]
    public void OneSubjectHasSixAnswersAndTheThreeGradesSitInsideThem()
    {
        Assert.Equal(6, QualityStandings.All.Count);
        Assert.Equal(QualityStandings.All.Order(), Enum.GetValues<QualityStanding>().Order());
        Assert.Equal(
            [QualityStanding.Good, QualityStanding.Warning, QualityStanding.MayNotBeWatchable],
            QualityStandings.All.Where(QualityStandings.WasMeasured));
    }

    [Fact]
    public void OnlyWhatWasMeasuredCanHaveGoneBeyondALevel()
        => Assert.Equal(
            [QualityStanding.Warning, QualityStanding.MayNotBeWatchable],
            QualityStandings.All.Where(QualityStandings.WentBeyond));

    [Fact(DisplayName = "BR-QD-001: nothing counted, nothing to count, no such statistic and no supply are four answers")]
    public void NothingCountedNothingToCountNoSuchStatisticAndNoSupplyAreFourAnswers()
        => Assert.Equal(
            [QualityStanding.Unmeasured, QualityStanding.Unsupported, QualityStanding.Unreachable],
            QualityStandings.All.Where(standing => !QualityStandings.WasMeasured(standing)));
}
