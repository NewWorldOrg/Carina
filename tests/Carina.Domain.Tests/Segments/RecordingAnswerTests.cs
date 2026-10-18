using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class RecordingAnswerTests
{
    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    [Fact(DisplayName = "BR-CV-001: an answer given is the first one sent, stands, and is confirmed at the moment it was sent when it says so")]
    public void AnAnswerGivenIsTheFirstOneSent()
    {
        RecordingId recording = RecordingId.New();
        AnsweredStretches stretches = Stretches();

        RecordingAnswer confirmed = RecordingAnswer.Given(recording, stretches, true, Noon);
        RecordingAnswer unconfirmed = RecordingAnswer.Given(recording, stretches, false, Noon);

        Assert.Equal((recording, 1, Noon, (DateTime?)null, true), (confirmed.RecordingId, confirmed.Answers, confirmed.AnsweredAt, confirmed.WithdrawnAt, confirmed.Stands));
        Assert.Equal(stretches.Stretches, confirmed.Segments);
        Assert.Equal(HalfAnHour, confirmed.Length);
        Assert.Equal((true, (DateTime?)Noon), (confirmed.Confirmed, confirmed.ConfirmedAt));
        Assert.Equal((false, (DateTime?)null), (unconfirmed.Confirmed, unconfirmed.ConfirmedAt));
    }

    [Fact(DisplayName = "BR-CD-002: a withdrawn answer is kept as it was, and no longer stands")]
    public void AWithdrawnAnswerNoLongerStands()
    {
        RecordingAnswer withdrawn = RecordingAnswer.Rehydrate(RecordingId.New(), Stretches().Stretches, HalfAnHour, false, null, Noon, 3, Noon.AddHours(1));

        Assert.False(withdrawn.Stands);
        Assert.Equal((3, (DateTime?)Noon.AddHours(1)), (withdrawn.Answers, withdrawn.WithdrawnAt));
    }

    [Fact(DisplayName = "the share of the length an answer gives to breaks")]
    public void TheShareTheAnswerGivesToBreaks()
        => Assert.Equal(90d / 1800, RecordingAnswer.Given(RecordingId.New(), Stretches(), false, Noon).BreakShare, 12);

    [Fact(DisplayName = "an answer is confirmed exactly when it says since when")]
    public void AnAnswerIsConfirmedExactlyWhenItSaysSinceWhen()
    {
        IReadOnlyList<SegmentSpan> stretches = Stretches().Stretches;

        Assert.Throws<ArgumentException>(() => RecordingAnswer.Rehydrate(RecordingId.New(), stretches, HalfAnHour, true, null, Noon, 1, null));
        Assert.Throws<ArgumentException>(() => RecordingAnswer.Rehydrate(RecordingId.New(), stretches, HalfAnHour, false, Noon, Noon, 1, null));
    }

    [Fact(DisplayName = "an answer covers its recording end to end from zero to its length, with no probability, and was sent at least once")]
    public void AnAnswerCoversItsRecordingEndToEnd()
    {
        SegmentSpan programme = new(TimeSpan.Zero, TimeSpan.FromSeconds(900), SegmentKind.Programme, null);
        SegmentSpan rest = new(TimeSpan.FromSeconds(900), HalfAnHour, SegmentKind.Break, null);
        SegmentSpan apart = new(TimeSpan.FromSeconds(901), HalfAnHour, SegmentKind.Break, null);
        SegmentSpan likely = new(TimeSpan.FromSeconds(900), HalfAnHour, SegmentKind.Break, 0.9);

        Assert.Throws<ArgumentException>(() => Rehydrated([]));
        Assert.Throws<ArgumentException>(() => Rehydrated([programme]));
        Assert.Throws<ArgumentException>(() => Rehydrated([programme, apart]));
        Assert.Throws<ArgumentException>(() => Rehydrated([programme, likely]));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordingAnswer.Rehydrate(RecordingId.New(), [programme, rest], HalfAnHour, false, null, Noon, 0, null));
        Assert.Equal(2, Rehydrated([programme, rest]).Segments.Count);
    }

    [Fact(DisplayName = "an answer keeps its times in UTC")]
    public void AnAnswerKeepsItsTimesInUtc()
        => Assert.Throws<ArgumentException>(() => RecordingAnswer.Given(RecordingId.New(), Stretches(), false, DateTime.SpecifyKind(Noon, DateTimeKind.Local)));

    private static RecordingAnswer Rehydrated(IReadOnlyList<SegmentSpan> segments)
        => RecordingAnswer.Rehydrate(RecordingId.New(), segments, HalfAnHour, false, null, Noon, 1, null);

    private static AnsweredStretches Stretches()
        => AnsweredStretches.Read(
            [new SentStretch(0, 912.3, SegmentKind.Programme), new SentStretch(912.3, 1002.3, SegmentKind.Break), new SentStretch(1002.3, 1800, SegmentKind.Programme)],
            HalfAnHour).Stretches!;
}
