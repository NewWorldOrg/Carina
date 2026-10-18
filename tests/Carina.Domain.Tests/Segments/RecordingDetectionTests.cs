using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class RecordingDetectionTests
{
    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    [Fact(DisplayName = "BR-CS-002: a detection is kept with how it ended, its stretches, the share breaks take and the candidates' seconds")]
    public void ADetectionIsKeptAsItWasJudged()
    {
        RecordingId recording = RecordingId.New();
        SegmentDetection judged = SegmentDetection.Judged(Tiled(300, 60, 1440), HalfAnHour, [TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(360.004)]);

        RecordingDetection kept = RecordingDetection.Of(recording, 1, judged, 2, Noon.AddMinutes(-1), Noon);

        Assert.Equal(recording, kept.RecordingId);
        Assert.Equal(1, kept.ModelVersion);
        Assert.Equal(SegmentDetectionState.Marked, kept.State);
        Assert.Equal(judged.Segments, kept.Segments);
        Assert.Equal(60d / 1800, kept.CmShare, 12);
        Assert.Equal([TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(360)], kept.Snaps);
        Assert.Equal(2, kept.ExtractionVersionNumber);
        Assert.Equal((Noon.AddMinutes(-1), Noon), (kept.CatalogUntil, kept.DetectedAt));
    }

    [Fact(DisplayName = "BR-CS-002: a sequence thrown away is kept as it was, with the share breaks took")]
    public void ASequenceThrownAwayIsKept()
    {
        SegmentDetection judged = SegmentDetection.Judged(Tiled(600, 1200), HalfAnHour, []);

        RecordingDetection kept = RecordingDetection.Of(RecordingId.New(), 1, judged, 2, Noon, Noon);

        Assert.Equal(SegmentDetectionState.Discarded, kept.State);
        Assert.Equal(2, kept.Segments.Count);
        Assert.Equal(1200d / 1800, kept.CmShare, 12);
    }

    [Fact(DisplayName = "a detection read back covers its recording from zero end to end, and one that marked breaks holds two stretches or more")]
    public void ADetectionReadBackIsWhole()
    {
        Assert.Throws<ArgumentException>(() => Rehydrated(SegmentDetectionState.NothingFound, [Span(1, 1800, SegmentKind.Programme)]));
        Assert.Throws<ArgumentException>(
            () => Rehydrated(SegmentDetectionState.Marked, [Span(0, 300, SegmentKind.Programme), Span(301, 1800, SegmentKind.Break)]));
        Assert.Throws<ArgumentException>(() => Rehydrated(SegmentDetectionState.Marked, [Span(0, 1800, SegmentKind.Break)]));
        Assert.Throws<ArgumentException>(() => Rehydrated(SegmentDetectionState.NothingFound, []));
        Assert.Throws<ArgumentException>(
            () => Rehydrated(SegmentDetectionState.NothingFound, [Span(0, 1800, SegmentKind.Programme)], [TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(3)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrated(SegmentDetectionState.NothingFound, [Span(0, 1800, SegmentKind.Programme)], cmShare: 1.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrated((SegmentDetectionState)7, [Span(0, 1800, SegmentKind.Programme)]));
        Assert.Throws<ArgumentException>(() => Rehydrated(SegmentDetectionState.NothingFound, [Span(0, 1800, SegmentKind.Programme)], at: DateTime.SpecifyKind(Noon, DateTimeKind.Local)));
    }

    [Theory(DisplayName = "a recording waits to be detected once its data was read and matched, and only while no detection by the version in use was made since")]
    [InlineData(LearningExtractionState.Done, true)]
    [InlineData(LearningExtractionState.Partial, true)]
    [InlineData(LearningExtractionState.Following, false)]
    [InlineData(LearningExtractionState.Waiting, false)]
    [InlineData(LearningExtractionState.Reading, false)]
    [InlineData(LearningExtractionState.Failed, false)]
    public void ARecordingWaitsToBeDetectedOnceReadAndMatched(LearningExtractionState state, bool awaited)
    {
        LearningExtraction record = Record(state, matchedAt: state is LearningExtractionState.Done or LearningExtractionState.Partial ? Noon : null);

        Assert.Equal(awaited, RecordingDetection.IsAwaited(record, null));
    }

    [Fact(DisplayName = "a recording whose data still waits to be matched does not wait to be detected")]
    public void ARecordingAwaitingMatchingIsNotDetectedYet()
    {
        Assert.False(RecordingDetection.IsAwaited(Record(LearningExtractionState.Done, matchedAt: null), null));
        Assert.False(RecordingDetection.IsAwaited(Record(LearningExtractionState.Done, matchedAt: Noon.AddMinutes(-20), updatedAt: Noon.AddMinutes(-10)), null));
    }

    [Fact(DisplayName = "a recording detected since it was matched waits no longer, and waits again once it is matched again")]
    public void ADetectionMadeSinceTheMatchIsEnough()
    {
        LearningExtraction record = Record(LearningExtractionState.Done, matchedAt: Noon);

        Assert.False(RecordingDetection.IsAwaited(record, Detected(record.RecordingId, Noon)));
        Assert.False(RecordingDetection.IsAwaited(record, Detected(record.RecordingId, Noon.AddMinutes(1))));
        Assert.True(RecordingDetection.IsAwaited(record, Detected(record.RecordingId, Noon.AddMinutes(-1))));
        Assert.Throws<ArgumentException>(() => RecordingDetection.IsAwaited(record, Detected(RecordingId.New(), Noon)));
    }

    [Fact(DisplayName = "a recording whose data reached no further than zero has nothing to detect")]
    public void NothingReadIsNothingToDetect()
    {
        Assert.False(RecordingDetection.IsAwaited(Record(LearningExtractionState.Done, matchedAt: Noon, readThrough: TimeSpan.Zero), null));
    }

    private static RecordingDetection Detected(RecordingId recording, DateTime at)
        => RecordingDetection.Of(recording, 1, SegmentDetection.Judged(Tiled(1800), HalfAnHour, []), 2, at, at);

    private static RecordingDetection Rehydrated(
        SegmentDetectionState state,
        IReadOnlyList<SegmentSpan> segments,
        IReadOnlyList<TimeSpan>? snaps = null,
        double cmShare = 0,
        DateTime? at = null)
        => RecordingDetection.Rehydrate(RecordingId.New(), 1, state, segments, cmShare, snaps ?? [], 2, Noon, at ?? Noon);

    private static LearningExtraction Record(
        LearningExtractionState state,
        DateTime? matchedAt,
        DateTime? updatedAt = null,
        TimeSpan? readThrough = null)
        => LearningExtraction.Rehydrate(
            RecordingId.New(),
            state,
            state is LearningExtractionState.Waiting ? null : ExtractionVersion.Current,
            readThrough ?? HalfAnHour,
            [],
            null,
            state is LearningExtractionState.Failed ? new ExtractionFailureDetail(ExtractionFailure.Other, "unreadable") : null,
            state is LearningExtractionState.Failed ? 1 : 0,
            Programme(),
            Noon.AddHours(-1),
            updatedAt ?? Noon.AddMinutes(-10),
            matchedAt);

    private static ProgrammeCopy Programme()
        => new(new NetworkId(1), new ServiceId(1), Noon.AddHours(-1), Noon.AddMinutes(-30), Noon.AddHours(-1), "架空の物語", [], [], null, AudioMode.Stereo, null);

    private static SegmentSpan Span(double starts, double ends, SegmentKind kind)
        => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends), kind, null);

    private static SegmentSpan[] Tiled(params double[] lengths)
    {
        List<SegmentSpan> spans = [];
        TimeSpan at = TimeSpan.Zero;

        for (int index = 0; index < lengths.Length; index++)
        {
            TimeSpan next = at + TimeSpan.FromSeconds(lengths[index]);
            spans.Add(new SegmentSpan(at, next, index % 2 is 0 ? SegmentKind.Programme : SegmentKind.Break, 0.9));
            at = next;
        }

        return [.. spans];
    }
}
