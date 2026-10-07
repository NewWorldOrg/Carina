using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class LearningDataGapsTests
{
    [Fact(DisplayName = "the gap the sound finds and the gap the frames find over the same stretch are one gap")]
    public void TheSameStretchFoundTwiceIsOneGap()
    {
        LearningDataGaps gaps = new();

        gaps.Add(Gap(12.03, 30.0));
        gaps.Add(Gap(12.045, 29.98));

        Assert.Equal([Gap(12.03, 30.0)], gaps.TakeAll());
    }

    [Fact(DisplayName = "gaps found out of order are handed out in order, joined where they overlap or touch")]
    public void GapsAreHandedOutInOrderJoinedWhereTheyMeet()
    {
        LearningDataGaps gaps = new();

        gaps.Add(Gap(50, 60));
        gaps.Add(Gap(10, 20));
        gaps.Add(Gap(20, 25));
        gaps.Add(Gap(55, 70));
        gaps.Add(null);

        Assert.Equal([Gap(10, 25), Gap(50, 70)], gaps.TakeAll());
        Assert.Empty(gaps.TakeAll());
    }

    [Fact(DisplayName = "only the gaps that end by the time given are taken out, and the rest wait")]
    public void OnlyTheGapsThatEndByTheTimeGivenAreTakenOut()
    {
        LearningDataGaps gaps = new();
        gaps.Add(Gap(10, 20));
        gaps.Add(Gap(30, 40));

        Assert.Equal([Gap(10, 20)], gaps.TakeThrough(TimeSpan.FromSeconds(35)));

        gaps.Add(Gap(38, 45));

        Assert.Empty(gaps.TakeThrough(TimeSpan.FromSeconds(44)));
        Assert.Equal([Gap(30, 45)], gaps.TakeThrough(TimeSpan.FromSeconds(45)));
    }

    private static LearningDataGap Gap(double from, double until)
        => new(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(until));
}
