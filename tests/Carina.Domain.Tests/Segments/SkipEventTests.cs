using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SkipEventTests
{
    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    [Fact(DisplayName = "BR-CV-003: a skip event keeps what the viewer did, the kind of the stretch, the two moments and the version, and nothing of who did it")]
    public void ASkipEventKeepsWhatTheViewerDid()
    {
        RecordingId recording = RecordingId.New();

        SkipEvent skip = SkipEvent.Of(
            recording,
            new ViewerAction(Noon, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(915), TimeSpan.FromSeconds(1002.3), 3));

        Assert.Equal(
            (0L, recording, Noon, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(915), TimeSpan.FromSeconds(1002.3), (int?)3),
            (skip.Id, skip.RecordingId, skip.HappenedAt, skip.Event, skip.Kind, skip.From, skip.To, skip.ModelVersion));
        Assert.DoesNotContain(
            typeof(SkipEvent).GetProperties(),
            property => property.Name.Contains("Viewer", StringComparison.Ordinal) || property.Name.Contains("User", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "BR-CV-003: a skip event from a chapter an encode marked names no version")]
    public void ASkipEventFromAnAnswerNamesNoVersion()
        => Assert.Null(SkipEvent.Rehydrate(7, RecordingId.New(), Noon, ViewerActionKind.Returned, SegmentKind.Ending, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(5), null).ModelVersion);

    [Fact(DisplayName = "BR-CV-003: a skip button offers a break, an opening or an ending, never the programme, and names moments from the recording's zero")]
    public void ASkipButtonNeverOffersTheProgramme()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SkipEvent.Rehydrate(1, RecordingId.New(), Noon, ViewerActionKind.Skipped, SegmentKind.Programme, TimeSpan.Zero, TimeSpan.FromSeconds(1), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkipEvent.Rehydrate(1, RecordingId.New(), Noon, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SkipEvent.Rehydrate(1, RecordingId.New(), Noon, (ViewerActionKind)9, SegmentKind.Break, TimeSpan.Zero, TimeSpan.FromSeconds(1), 1));
    }

    [Theory(DisplayName = "BR-CV-003: a moment is read to the hundredth of a second from the recording's zero to its length, and none outside it")]
    [InlineData(0, 0)]
    [InlineData(915.004, 915)]
    [InlineData(1800, 1800)]
    [InlineData(1800.004, 1800)]
    [InlineData(-0.004, 0)]
    public void AMomentIsReadWithinTheRecording(double seconds, double read)
        => Assert.Equal(TimeSpan.FromSeconds(read), SkipEvent.Moment(seconds, HalfAnHour));

    [Theory(DisplayName = "BR-CV-003: a moment before the recording's zero or past its length is none")]
    [InlineData(-0.01)]
    [InlineData(1800.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AMomentOutsideTheRecordingIsNone(double seconds)
        => Assert.Null(SkipEvent.Moment(seconds, HalfAnHour));
}
