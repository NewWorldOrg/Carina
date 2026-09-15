using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class EncodeJobRunnerWatermarkTests
{
    private const string WritesThePicture = "printf 'the picture' > \"$destination\"";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-ED2-007: the first recording of a service is looked at with no watermark, and a recording is never looked at with the one learned from itself")]
    public async Task TheFirstRecordingIsLookedAtWithNoneAndNoneLearnedFromItself()
    {
        using var harness = new EncodeHarness();
        harness.Standing(WritesThePicture);
        var looked = new ScriptedChapters();
        harness.ChapterDetector = looked;
        Recording recording = harness.Recorded();

        await harness.Runner.RunAsync(harness.Running(recording.Id, harness.Defined().Id), Cancel);

        harness.Watermarks.Kept.Add(StationWatermark.Learn(
            recording.NetworkId,
            recording.ServiceId,
            recording.Id,
            Covering(5),
            EncodeHarness.Started));

        await harness.Runner.RunAsync(harness.Running(recording.Id, harness.Defined().Id), Cancel);

        Assert.Equal(2, looked.LearnedAhead.Count);
        Assert.All(looked.LearnedAhead, Assert.Null);
        Assert.Equal([recording.Id, recording.Id], harness.Watermarks.Asked);
    }

    [Fact(DisplayName = "BR-ED2-007: a recording is looked at with the watermark learned ahead from another recording of its service")]
    public async Task ARecordingIsLookedAtWithTheWatermarkLearnedAhead()
    {
        using var harness = new EncodeHarness();
        harness.Standing(WritesThePicture);
        var looked = new ScriptedChapters();
        harness.ChapterDetector = looked;
        Recording earlier = harness.Recorded();
        Recording judged = harness.Recorded();
        harness.Watermarks.Kept.Add(StationWatermark.Learn(
            earlier.NetworkId,
            earlier.ServiceId,
            earlier.Id,
            Covering(7),
            EncodeHarness.Started));

        await harness.Runner.RunAsync(harness.Running(judged.Id, harness.Defined().Id), Cancel);

        WatermarkMask? handed = Assert.Single(looked.LearnedAhead);
        Assert.NotNull(handed);
        Assert.Equal(Covering(7).Packed(), handed.Packed());
    }

    [Fact(DisplayName = "BR-ED2-007: what the look learned is kept against the recording it was learned from, once the reading is in the ledger")]
    public async Task WhatTheLookLearnedIsKeptAgainstTheRecordingItWasLearnedFrom()
    {
        using var harness = new EncodeHarness();
        harness.Standing(WritesThePicture);
        harness.ChapterDetector = new ScriptedChapters { Answers = () => ChapterDetection.NothingFound().Learning(Covering(9)) };
        Recording recording = harness.Recorded();
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);
        ChapterVerdict? judgedWhenKept = null;
        harness.Jobs.WhenSaving = saving => judgedWhenKept ??= saving.Chapters?.Verdict;

        await harness.Runner.RunAsync(job, Cancel);

        StationWatermark kept = Assert.Single(harness.Watermarks.Kept);
        Assert.Equal(recording.Id, kept.LearnedFrom);
        Assert.Equal(recording.NetworkId, kept.NetworkId);
        Assert.Equal(recording.ServiceId, kept.ServiceId);
        Assert.Equal(harness.Clock.GetUtcNow().UtcDateTime, kept.LearnedAt);
        Assert.Equal(Covering(9).Packed(), kept.Pattern);
        Assert.Equal(ChapterVerdict.NothingFound, judgedWhenKept);
    }

    [Fact(DisplayName = "BR-ED2-007: a reading that learned nothing keeps nothing")]
    public async Task AReadingThatLearnedNothingKeepsNothing()
    {
        using var harness = new EncodeHarness();
        harness.Standing(WritesThePicture);
        harness.ChapterDetector = new ScriptedChapters { Answers = ChapterDetection.NothingFound };

        await harness.Runner.RunAsync(harness.Running(harness.Recorded().Id, harness.Defined().Id), Cancel);

        Assert.Empty(harness.Watermarks.Kept);
    }

    [Fact(DisplayName = "BR-ED2-007: watermarks that can be neither read nor kept fail nothing, and the job is looked at without one")]
    public async Task WatermarksThatCanBeNeitherReadNorKeptFailNothing()
    {
        using var harness = new EncodeHarness();
        harness.Standing(WritesThePicture);
        var looked = new ScriptedChapters { Answers = () => ChapterDetection.NothingFound().Learning(Covering(9)) };
        harness.ChapterDetector = looked;
        harness.Watermarks.Refusing = new InvalidOperationException("the ledger refused");

        EncodeJobStatus ended = await harness.Runner.RunAsync(harness.Running(harness.Recorded().Id, harness.Defined().Id), Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Null(Assert.Single(looked.LearnedAhead));
    }

    [Fact(DisplayName = "BR-ED2-007: a machine told not to watch for the watermark asks for none and hands the look none")]
    public async Task AMachineToldNotToWatchAsksForNone()
    {
        using var harness = new EncodeHarness();
        harness.Standing(WritesThePicture);
        harness.Settings = harness.Settings with { Chapters = harness.Settings.Chapters with { Watermark = false } };
        var looked = new ScriptedChapters();
        harness.ChapterDetector = looked;
        Recording earlier = harness.Recorded();
        harness.Watermarks.Kept.Add(StationWatermark.Learn(
            earlier.NetworkId,
            earlier.ServiceId,
            earlier.Id,
            Covering(7),
            EncodeHarness.Started));

        await harness.Runner.RunAsync(harness.Running(harness.Recorded().Id, harness.Defined().Id), Cancel);

        Assert.Null(Assert.Single(looked.LearnedAhead));
        Assert.Empty(harness.Watermarks.Asked);
    }

    [Fact(DisplayName = "BR-ED2-007: a machine told not to look for the breaks asks for no watermark either")]
    public async Task AMachineToldNotToLookAsksForNoWatermark()
    {
        using var harness = new EncodeHarness();
        harness.Standing(WritesThePicture);

        await harness.Runner.RunAsync(harness.Running(harness.Recorded().Id, harness.Defined().Id), Cancel);

        Assert.Empty(harness.Watermarks.Asked);
        Assert.Empty(harness.Watermarks.Kept);
    }

    private static WatermarkMask Covering(int pixels) => WatermarkMask.Covering([.. Enumerable.Range(0, pixels)]);
}
