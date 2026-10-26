using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SeekEventTests
{
    private static readonly DateTime Noon = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-CV-004: a seek keeps whether it went forward or came back, how it was made and the two moments, and nothing of who made it")]
    public void ASeekKeepsWhatTheViewerDid()
    {
        RecordingId recording = RecordingId.New();

        SeekEvent seek = SeekEvent.Of(recording, new ViewerSeek(Noon, SeekAction.Forward, SeekMeans.Step, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690)));

        Assert.Equal(
            (0L, recording, Noon, SeekAction.Forward, SeekMeans.Step, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690)),
            (seek.Id, seek.RecordingId, seek.HappenedAt, seek.Event, seek.Means, seek.From, seek.To));
        Assert.Equal(seek.Seek(), new ViewerSeek(Noon, SeekAction.Forward, SeekMeans.Step, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690)));
        Assert.DoesNotContain(
            typeof(SeekEvent).GetProperties(),
            property => property.Name.Contains("Viewer", StringComparison.Ordinal) || property.Name.Contains("User", StringComparison.Ordinal));
        Assert.Equal((SkipEvent.MostAnHour, SkipEvent.Within), (SeekEvent.MostAnHour, SeekEvent.Within));
    }

    [Theory(DisplayName = "BR-CV-004: a seek forward lands later than it started and coming back lands earlier, both from the recording's zero, made one of four ways")]
    [InlineData(SeekAction.Forward, SeekMeans.Bar, 600, 600)]
    [InlineData(SeekAction.Forward, SeekMeans.Bar, 600, 590)]
    [InlineData(SeekAction.Returned, SeekMeans.Key, 600, 610)]
    [InlineData(SeekAction.Forward, SeekMeans.Bar, -1, 10)]
    [InlineData((SeekAction)9, SeekMeans.Bar, 600, 690)]
    [InlineData(SeekAction.Forward, (SeekMeans)9, 600, 690)]
    public void ASeekGoesTheWayItSays(SeekAction action, SeekMeans means, double from, double to)
        => Assert.ThrowsAny<ArgumentException>(() => SeekEvent.Rehydrate(1, RecordingId.New(), Noon, action, means, TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to)));
}
