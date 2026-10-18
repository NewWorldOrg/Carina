using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class LearningExtractionMatchingTests
{
    private static readonly DateTime Noon = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ExtractionVersion Newer = new(LearningData.ExtractionVersion + 1, ExtractionOrigin.RecordingFile);

    [Theory(DisplayName = "a record read to its end or as far as it could be waits to be matched against the catalog, and no other does")]
    [InlineData(LearningExtractionState.Done, true)]
    [InlineData(LearningExtractionState.Partial, true)]
    [InlineData(LearningExtractionState.Following, false)]
    [InlineData(LearningExtractionState.Waiting, false)]
    [InlineData(LearningExtractionState.Reading, false)]
    [InlineData(LearningExtractionState.Failed, false)]
    public void ARecordReadWaitsToBeMatched(LearningExtractionState state, bool awaits)
    {
        Assert.Equal(awaits, In(state).AwaitsMatching);
    }

    [Fact(DisplayName = "a record matched waits no longer, and waits again once its data is read again")]
    public void AMatchedRecordWaitsAgainOnceReadAgain()
    {
        LearningExtraction record = In(LearningExtractionState.Done);

        record.Matched(Noon.AddMinutes(5));

        Assert.False(record.AwaitsMatching);
        Assert.Equal(Noon.AddMinutes(5), record.MatchedAt);
        Assert.Equal(Noon, record.UpdatedAt);

        record.Outdate(Newer, Noon.AddMinutes(10));
        record.Read(Newer, Noon.AddMinutes(10));
        record.Finish(Noon.AddMinutes(20));

        Assert.True(record.AwaitsMatching);
    }

    [Fact(DisplayName = "a record matched at a moment before it last changed counts as matched when it last changed")]
    public void AnEarlyMatchCountsAsMatchedWhenItLastChanged()
    {
        LearningExtraction record = In(LearningExtractionState.Partial);

        record.Matched(Noon.AddMinutes(-5));

        Assert.Equal(Noon, record.MatchedAt);
        Assert.False(record.AwaitsMatching);
    }

    [Theory(DisplayName = "a record that does not wait to be matched is not matched")]
    [InlineData(LearningExtractionState.Following)]
    [InlineData(LearningExtractionState.Waiting)]
    [InlineData(LearningExtractionState.Reading)]
    [InlineData(LearningExtractionState.Failed)]
    public void ARecordNotWaitingIsNotMatched(LearningExtractionState state)
    {
        Assert.Throws<InvalidOperationException>(() => In(state).Matched(Noon.AddMinutes(5)));
    }

    [Fact(DisplayName = "a record kept as matched before it last changed waits to be matched again")]
    public void ARecordMatchedBeforeItChangedWaits()
    {
        Assert.True(Rehydrated(Noon.AddMinutes(-1), Noon).AwaitsMatching);
        Assert.False(Rehydrated(Noon, Noon).AwaitsMatching);
    }

    [Fact(DisplayName = "the time a record was matched is in UTC and no earlier than the record was made")]
    public void TheTimeMatchedIsInUtcAndAfterTheRecordWasMade()
    {
        Assert.Throws<ArgumentException>(() => Rehydrated(DateTime.SpecifyKind(Noon, DateTimeKind.Local), Noon));
        Assert.Throws<ArgumentException>(() => Rehydrated(Noon.AddMinutes(-11), Noon));
        Assert.Throws<ArgumentException>(() => In(LearningExtractionState.Done).Matched(DateTime.SpecifyKind(Noon, DateTimeKind.Local)));
    }

    private static LearningExtraction Rehydrated(DateTime matchedAt, DateTime updatedAt)
        => LearningExtraction.Rehydrate(
            RecordingId.New(),
            LearningExtractionState.Done,
            ExtractionVersion.Current,
            TimeSpan.FromMinutes(30),
            [],
            null,
            null,
            0,
            Programme(),
            Noon.AddMinutes(-10),
            updatedAt,
            matchedAt);

    private static LearningExtraction In(LearningExtractionState state)
    {
        RecordingId recording = RecordingId.New();

        if (state is LearningExtractionState.Following)
        {
            return LearningExtraction.Following(recording, Programme(), ExtractionVersion.Current, Noon);
        }

        LearningExtraction extraction = LearningExtraction.Waiting(recording, Programme(), Noon);

        if (state is LearningExtractionState.Waiting)
        {
            return extraction;
        }

        extraction.Read(ExtractionVersion.Current, Noon);

        switch (state)
        {
            case LearningExtractionState.Done:
                extraction.Finish(Noon);
                break;
            case LearningExtractionState.Partial:
                extraction.FinishPartway(Noon);
                break;
            case LearningExtractionState.Failed:
                extraction.Fail(ExtractionFailure.Other, "it stopped", Noon);
                break;
        }

        return extraction;
    }

    private static ProgrammeCopy Programme()
        => new(
            new NetworkId(40_001),
            new ServiceId(60_001),
            Noon,
            Noon.AddMinutes(30),
            Noon.AddMinutes(-1),
            "架空の番組",
            [],
            [],
            null,
            AudioMode.Stereo,
            null);
}
