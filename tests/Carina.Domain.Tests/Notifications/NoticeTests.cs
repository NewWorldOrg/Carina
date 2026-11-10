using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Notifications;

public sealed class NoticeTests
{
    private static readonly DateTime At = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-ND-007: a notice holds its title, body, link and tone from when it is taken")]
    public void ANoticeHoldsItsWordsFromWhenItIsTaken()
    {
        Guid source = Guid.NewGuid();

        Notice notice = Notice.Take(
            NoticeId.New(),
            NoticeKind.RecordingFailed,
            source,
            At,
            new NoticeWords("A title", "A body", "https://screens.example/recordings/1"),
            At.AddSeconds(15));

        Assert.Equal(NoticeKind.RecordingFailed, notice.Kind);
        Assert.Equal(source, notice.SourceId);
        Assert.Equal(NoticeTone.Failure, notice.Tone);
        Assert.Equal("A title", notice.Title);
        Assert.Equal("A body", notice.Body);
        Assert.Equal("https://screens.example/recordings/1", notice.Link);
        Assert.Equal(At, notice.OccurredAt);
        Assert.Equal(At.AddSeconds(15), notice.TakenAt);
    }

    [Theory(DisplayName = "the tone of a notice follows how what it tells turned out")]
    [InlineData(NoticeKind.RecordingStarted, NoticeTone.Informative)]
    [InlineData(NoticeKind.RecordingCompleted, NoticeTone.Good)]
    [InlineData(NoticeKind.RecordingTruncated, NoticeTone.Caution)]
    [InlineData(NoticeKind.RecordingFailed, NoticeTone.Failure)]
    [InlineData(NoticeKind.EncodeStarted, NoticeTone.Informative)]
    [InlineData(NoticeKind.EncodeCompleted, NoticeTone.Good)]
    [InlineData(NoticeKind.EncodeFailed, NoticeTone.Failure)]
    [InlineData(NoticeKind.ReservationCompeting, NoticeTone.Caution)]
    [InlineData(NoticeKind.ReservationMissed, NoticeTone.Failure)]
    [InlineData(NoticeKind.ProgrammeMoved, NoticeTone.Caution)]
    [InlineData(NoticeKind.QualityIncidentDetected, NoticeTone.Caution)]
    [InlineData(NoticeKind.QualityIncidentResolved, NoticeTone.Good)]
    [InlineData(NoticeKind.DeviceDisconnected, NoticeTone.Failure)]
    public void TheToneFollowsTheKind(NoticeKind kind, NoticeTone tone)
    {
        Assert.Equal(tone, NoticeTones.Of(kind));
    }

    [Fact]
    public void EveryKindHasATone()
    {
        Assert.All(Enum.GetValues<NoticeKind>(), kind => Assert.True(Enum.IsDefined(NoticeTones.Of(kind))));
    }

    [Fact(DisplayName = "a notice with no link is kept, and one whose link is not an http address is not")]
    public void ANoticeMayCarryNoLink()
    {
        Assert.Null(Taken(new NoticeWords("A title", "A body", null)).Link);
        Assert.Throws<ArgumentException>(() => Taken(new NoticeWords("A title", "A body", "/recordings/1")));
        Assert.Throws<ArgumentException>(() => Taken(new NoticeWords("A title", "A body", "file:///recordings/1")));
    }

    [Fact(DisplayName = "a notice is not kept without a title, a body, or the row it was taken from")]
    public void ANoticeIsNotKeptWithoutItsParts()
    {
        Assert.Throws<ArgumentException>(() => Taken(new NoticeWords(" ", "A body", null)));
        Assert.Throws<ArgumentException>(() => Taken(new NoticeWords("A title", string.Empty, null)));
        Assert.Throws<ArgumentException>(() => Taken(new NoticeWords(new string('a', Notice.TitleLongest + 1), "A body", null)));
        Assert.Throws<ArgumentException>(() => Taken(new NoticeWords("A title", new string('a', Notice.BodyLongest + 1), null)));
        Assert.Throws<ArgumentException>(
            () => Notice.Take(NoticeId.New(), NoticeKind.RecordingFailed, Guid.Empty, At, new NoticeWords("A title", "A body", null), At));
    }

    private static Notice Taken(NoticeWords words)
        => Notice.Take(NoticeId.New(), NoticeKind.RecordingFailed, Guid.NewGuid(), At, words, At);
}
