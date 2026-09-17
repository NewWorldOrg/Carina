using Carina.Domain.Auth;
using Carina.Domain.Recordings;
using Carina.Domain.Viewing;

namespace Carina.Domain.Tests.Viewing;

public sealed class PlaybackPositionTests
{
    private static readonly DateTime Noon = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WhereTheWatchingGotToIsKeptToTheMillisecond()
    {
        PlaybackPosition reached = PlaybackPosition.Reached(
            RecordingId.New(),
            new Subject("someone"),
            TimeSpan.FromSeconds(123.456),
            Noon);

        Assert.Equal(123_456, reached.PositionMs);
        Assert.Equal(TimeSpan.FromSeconds(123.456), reached.Position);
        Assert.Equal(Noon, reached.UpdatedAt);
    }

    [Fact]
    public void TheBeginningIsAPositionLikeAnyOther()
    {
        PlaybackPosition reached = PlaybackPosition.Reached(
            RecordingId.New(),
            new Subject("someone"),
            TimeSpan.Zero,
            Noon);

        Assert.Equal(0, reached.PositionMs);
        Assert.Equal(TimeSpan.Zero, reached.Position);
    }

    [Fact]
    public void WatchingCannotHaveGotToSomewhereBeforeTheBeginning()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackPosition.Reached(
            RecordingId.New(),
            new Subject("someone"),
            TimeSpan.FromSeconds(-1),
            Noon));

        Assert.Throws<ArgumentOutOfRangeException>(() => PlaybackPosition.Rehydrate(
            RecordingId.New(),
            new Subject("someone"),
            -1,
            Noon));
    }

    [Fact]
    public void WhenTheWatchingGotThereIsKeptInUtcLikeEveryOtherTime()
    {
        Assert.Throws<ArgumentException>(() => PlaybackPosition.Reached(
            RecordingId.New(),
            new Subject("someone"),
            TimeSpan.FromMinutes(1),
            new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void APositionNamesBothTheRecordingItIsInAndTheViewerItBelongsTo()
    {
        RecordingId recording = RecordingId.New();
        var viewer = new Subject("someone");

        PlaybackPosition reached = PlaybackPosition.Rehydrate(recording, viewer, 5_000, Noon);

        Assert.Equal(recording, reached.RecordingId);
        Assert.Equal(viewer, reached.Viewer);
    }
}
