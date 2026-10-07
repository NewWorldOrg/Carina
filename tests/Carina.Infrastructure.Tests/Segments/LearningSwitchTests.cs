using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class LearningSwitchTests
{
    private static readonly DateTime Noon = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "with no row held learning is off")]
    public async Task WithNoRowHeldLearningIsOff()
    {
        var rows = new HeldSegmentSettings();

        Assert.False(await new LearningSwitch(rows).IsOnAsync(Cancel));
    }

    [Theory(DisplayName = "a row held answers whether learning is on")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ARowHeldAnswersWhetherLearningIsOn(bool learning)
    {
        var rows = new HeldSegmentSettings();
        await rows.SaveAsync(SegmentSettings.LearningSwitched(learning, Noon), Cancel);

        Assert.Equal(learning, await new LearningSwitch(rows).IsOnAsync(Cancel));
    }

    [Fact(DisplayName = "turning learning off is the answer on the very next ask")]
    public async Task TurningLearningOffIsTheAnswerOnTheVeryNextAsk()
    {
        var rows = new HeldSegmentSettings();
        var learningSwitch = new LearningSwitch(rows);
        await rows.SaveAsync(SegmentSettings.LearningSwitched(true, Noon), Cancel);

        Assert.True(await learningSwitch.IsOnAsync(Cancel));

        await rows.SaveAsync(SegmentSettings.LearningSwitched(false, Noon.AddSeconds(1)), Cancel);

        Assert.False(await learningSwitch.IsOnAsync(Cancel));
        Assert.Equal(2, rows.Reads);
    }
}
