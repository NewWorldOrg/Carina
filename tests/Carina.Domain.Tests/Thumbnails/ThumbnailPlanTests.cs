using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;

namespace Carina.Domain.Tests.Thumbnails;

public sealed class ThumbnailPlanTests
{
    private static readonly ThumbnailSettings Settings = new();

    [Fact]
    public void ARecordingThatFailedGetsNoPicture()
    {
        ThumbnailPlan plan = ThumbnailPlan.For(Subject(RecordingOutcome.Failed), Settings);

        Assert.Equal(ThumbnailIntent.Skip, plan.Intent);
        Assert.Equal(TimeSpan.Zero, plan.At);
        Assert.False(plan.OfSomethingUnfinished);
    }

    [Fact]
    public void ARecordingThatFinishedGetsOne()
    {
        ThumbnailPlan plan = ThumbnailPlan.For(Subject(RecordingOutcome.Complete), Settings);

        Assert.Equal(ThumbnailIntent.Draw, plan.Intent);
        Assert.Equal(TimeSpan.FromSeconds(120), plan.At);
        Assert.False(plan.OfSomethingUnfinished);
    }

    [Fact]
    public void ARecordingThatIsCutShortGetsOneThatSaysSo()
    {
        ThumbnailPlan plan = ThumbnailPlan.For(Subject(RecordingOutcome.Truncated), Settings);

        Assert.Equal(ThumbnailIntent.Draw, plan.Intent);
        Assert.Equal(TimeSpan.FromSeconds(120), plan.At);
        Assert.True(plan.OfSomethingUnfinished);
    }

    [Fact]
    public void ThePositionComesFromWhatWasWrittenAndNotFromTheWindow()
    {
        ThumbnailPlan plan = ThumbnailPlan.For(
            Subject(RecordingOutcome.Truncated, TimeSpan.FromSeconds(90)),
            Settings);

        Assert.Equal(TimeSpan.FromSeconds(30), plan.At);
    }

    [Theory]
    [InlineData(180, 60)]
    [InlineData(7200, 120)]
    public void APictureIsPlannedAThirdOfTheWayInUntilTheCapTakesOver(int written, int expected)
        => Assert.Equal(
            TimeSpan.FromSeconds(expected),
            ThumbnailPlan.For(
                Subject(RecordingOutcome.Complete, TimeSpan.FromSeconds(written)),
                Settings).At);

    [Fact]
    public void ARecordingThatSucceededWithNoLengthWrittenDownIsStillNotDrawnFromItsHead()
    {
        ThumbnailPlan plan = ThumbnailPlan.For(
            Subject(RecordingOutcome.Complete, TimeSpan.Zero),
            Settings);

        Assert.Equal(ThumbnailIntent.Draw, plan.Intent);
        Assert.Equal(TimeSpan.FromSeconds(120), plan.At);
    }

    [Fact]
    public void ARecordingThatWroteNothingAtAllFailedAndIsSkippedBeforeAPositionIsAskedFor()
    {
        ThumbnailPlan plan = ThumbnailPlan.For(Subject(RecordingOutcome.Failed, TimeSpan.Zero), Settings);

        Assert.Equal(ThumbnailIntent.Skip, plan.Intent);
        Assert.Equal(TimeSpan.Zero, plan.At);
    }

    [Fact]
    public void APlanWithoutASubjectOrSettingsIsRefused()
    {
        Assert.Equal(
            "subject",
            Assert.Throws<ArgumentNullException>(() => ThumbnailPlan.For(null!, Settings)).ParamName);
        Assert.Equal(
            "settings",
            Assert.Throws<ArgumentNullException>(
                () => ThumbnailPlan.For(Subject(RecordingOutcome.Complete), null!)).ParamName);
    }

    private static ThumbnailSubject Subject(RecordingOutcome outcome, TimeSpan? written = null)
    {
        RecordingId id = RecordingId.New();

        return new ThumbnailSubject(
            id,
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            new ServiceId(1032),
            outcome,
            written ?? TimeSpan.FromHours(2));
    }
}
