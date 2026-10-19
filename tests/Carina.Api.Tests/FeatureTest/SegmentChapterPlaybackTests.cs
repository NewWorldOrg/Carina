using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

public sealed class SegmentChapterPlaybackTests
{
    private static readonly EncodeTimeline HalfASecondSkipped = new(TimeSpan.FromSeconds(1.4), TimeSpan.FromSeconds(0.5), TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(1799.5));

    [Fact(DisplayName = "BR-CD-001: the plan of an artefact carries the chapters the version in use detected, on the artefact's clock, a likely break with something after it offered to be skipped")]
    public async Task TheArtefactsPlanCarriesTheDetectedChaptersOnItsClock()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: HalfASecondSkipped);
        await DetectedAsync(feature, recording.Id, (0, 912.3, SegmentKind.Programme, 0.98), (912.3, 1002.3, SegmentKind.Break, 0.93), (1002.3, 1800, SegmentKind.Programme, 0.99));

        JsonElement plan = await PlanAsync(feature, recording);
        JsonElement[] chapters = [.. plan.GetProperty("chapters").EnumerateArray()];

        Assert.Equal("direct", plan.GetProperty("route").GetString());
        Assert.Equal(
            [(0d, 911.8d, "programme", false), (911.8d, 1001.8d, "break", true), (1001.8d, 1799.5d, "programme", false)],
            chapters.Select(Read));
    }

    [Fact(DisplayName = "BR-CD-001: the plan of the recording itself carries the chapters the version in use detected at the recording's own time")]
    public async Task TheRecordingsOwnPlanCarriesTheDetectedChaptersAtItsOwnTime()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        await DetectedAsync(feature, recording.Id, (0, 912.3, SegmentKind.Programme, 0.98), (912.3, 1002.3, SegmentKind.Break, 0.93), (1002.3, 1800, SegmentKind.Programme, 0.99));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.True(plan.GetProperty("transcodes").GetBoolean());
        Assert.Equal(
            [(0d, 912.3d, "programme", false), (912.3d, 1002.3d, "break", true), (1002.3d, 1800d, "programme", false)],
            plan.GetProperty("chapters").EnumerateArray().Select(Read));
    }

    [Fact(DisplayName = "BR-CD-001: a recording with an artefact asked for as it was recorded carries the chapters at the recording's own time")]
    public async Task TheRecordingAskedForAsRecordedCarriesTheChaptersAtItsOwnTime()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: HalfASecondSkipped);
        await DetectedAsync(feature, recording.Id, (0, 300, SegmentKind.Programme, 0.98), (300, 360, SegmentKind.Break, 0.93), (360, 1800, SegmentKind.Programme, 0.99));

        JsonElement plan = await PlanAsync(feature, recording, "?source=recording");

        Assert.Equal("recording", plan.GetProperty("source").GetString());
        Assert.Equal([0d, 300d, 360d], plan.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("startsAtSec").GetDouble()));
    }

    [Fact(DisplayName = "BR-CD-009: a break less likely than the threshold is marked but not offered to be skipped, and neither is a break the recording ends on")]
    public async Task UnlikelyOrFinalBreaksAreNotOfferedToBeSkipped()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        await DetectedAsync(
            feature,
            recording.Id,
            (0, 300, SegmentKind.Programme, 0.98),
            (300, 360, SegmentKind.Break, 0.69),
            (360, 1740, SegmentKind.Programme, 0.99),
            (1740, 1800, SegmentKind.Break, 0.99));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal([false, false, false, false], plan.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("skippable").GetBoolean()));
        Assert.Equal("break", plan.GetProperty("chapters")[1].GetProperty("kind").GetString());
    }

    [Theory(DisplayName = "BR-CD-009: the threshold the settings hold decides which breaks are offered to be skipped")]
    [InlineData(0.60, true, true)]
    [InlineData(0.90, false, true)]
    [InlineData(0.95, false, false)]
    public async Task TheThresholdTheSettingsHoldDecidesWhichBreaksAreOffered(double threshold, bool unlikely, bool likely)
    {
        await using var feature = new PlayFeature();
        await feature.SegmentSettings.SaveAsync(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, false, null, threshold, RecordingFeature.Noon), CancellationToken.None);
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        await DetectedAsync(
            feature,
            recording.Id,
            (0, 300, SegmentKind.Programme, 0.98),
            (300, 360, SegmentKind.Break, 0.69),
            (360, 900, SegmentKind.Programme, 0.99),
            (900, 960, SegmentKind.Break, 0.93),
            (960, 1800, SegmentKind.Programme, 0.99));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal([false, unlikely, false, likely, false], plan.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("skippable").GetBoolean()));
    }

    [Fact(DisplayName = "BR-CD-009: an opening and an ending are played by their own kinds on the artefact and on the recording itself, each offered to be skipped when it is likely enough and something follows it")]
    public async Task AnOpeningAndAnEndingArePlayedByTheirOwnKinds()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: HalfASecondSkipped);
        await DetectedAsync(
            feature,
            recording.Id,
            (0, 60, SegmentKind.Programme, 0.98),
            (60, 150, SegmentKind.Opening, 0.92),
            (150, 900, SegmentKind.Programme, 0.99),
            (900, 960, SegmentKind.Break, 0.93),
            (960, 1710, SegmentKind.Programme, 0.99),
            (1710, 1800, SegmentKind.Ending, 0.95));

        JsonElement artefact = await PlanAsync(feature, recording);
        JsonElement original = await PlanAsync(feature, recording, "?source=recording");

        Assert.Equal(
            [
                (0d, 59.5d, "programme", false), (59.5d, 149.5d, "opening", true), (149.5d, 899.5d, "programme", false),
                (899.5d, 959.5d, "break", true), (959.5d, 1709.5d, "programme", false), (1709.5d, 1799.5d, "ending", false),
            ],
            artefact.GetProperty("chapters").EnumerateArray().Select(Read));
        Assert.Equal(
            [
                (0d, 60d, "programme", false), (60d, 150d, "opening", true), (150d, 900d, "programme", false),
                (900d, 960d, "break", true), (960d, 1710d, "programme", false), (1710d, 1800d, "ending", false),
            ],
            original.GetProperty("chapters").EnumerateArray().Select(Read));
    }

    [Theory(DisplayName = "BR-CD-009: the threshold the settings hold decides for an opening and an ending as it does for a break, and an ending with something after it is offered to be skipped")]
    [InlineData(0.70, false, true, true)]
    [InlineData(0.60, true, true, true)]
    [InlineData(0.90, false, false, true)]
    [InlineData(0.95, false, false, false)]
    public async Task TheThresholdDecidesForAnOpeningAndAnEnding(double threshold, bool opening, bool breaks, bool ending)
    {
        await using var feature = new PlayFeature();
        await feature.SegmentSettings.SaveAsync(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, false, null, threshold, RecordingFeature.Noon), CancellationToken.None);
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        await DetectedAsync(
            feature,
            recording.Id,
            (0, 90, SegmentKind.Opening, 0.65),
            (90, 900, SegmentKind.Programme, 0.99),
            (900, 960, SegmentKind.Break, 0.8),
            (960, 1680, SegmentKind.Programme, 0.99),
            (1680, 1770, SegmentKind.Ending, 0.93),
            (1770, 1800, SegmentKind.Programme, 0.9));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal(
            ["opening", "programme", "break", "programme", "ending", "programme"],
            plan.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("kind").GetString()));
        Assert.Equal(
            [opening, false, breaks, false, ending, false],
            plan.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("skippable").GetBoolean()));
    }

    [Fact(DisplayName = "BR-CD-013: while learning is off, an artefact of a recording not detected plays each chapter its encode marked as the kind it was marked")]
    public async Task WhileLearningIsOffEachMarkedChapterIsPlayedAsItsKind()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);
        await feature.MarkedAsync(
            feature.Jobs.Jobs[0],
            new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(90), ChapterKind.Opening),
            new ChapterSegment(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(900), ChapterKind.Programme),
            new ChapterSegment(TimeSpan.FromSeconds(900), TimeSpan.FromSeconds(960), ChapterKind.Break),
            new ChapterSegment(TimeSpan.FromSeconds(960), TimeSpan.FromSeconds(1710), ChapterKind.Programme),
            new ChapterSegment(TimeSpan.FromSeconds(1710), TimeSpan.FromSeconds(1799), ChapterKind.Ending));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal(
            [(0d, 90d, "opening", true), (90d, 900d, "programme", false), (900d, 960d, "break", true), (960d, 1710d, "programme", false), (1710d, 1799d, "ending", false)],
            plan.GetProperty("chapters").EnumerateArray().Select(Read));
    }

    [Fact(DisplayName = "BR-CS-002: a recording whose detected sequence was thrown away plays with no chapters, even with chapters its encode marked")]
    public async Task ASequenceThrownAwayPlaysWithNoChapters()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: HalfASecondSkipped);
        await feature.MarkedAsync(feature.Jobs.Jobs[0], new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(90), ChapterKind.Programme), new ChapterSegment(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(150), ChapterKind.Break), new ChapterSegment(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(300), ChapterKind.Programme));
        await DetectedAsync(feature, recording.Id, (0, 600, SegmentKind.Programme, 0.6), (600, 1800, SegmentKind.Break, 0.8));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Empty(plan.GetProperty("chapters").EnumerateArray());
    }

    [Fact(DisplayName = "BR-CD-013: while learning is on, a recording not detected plays with no chapters, whatever its encode marked")]
    public async Task WhileLearningIsOnANotDetectedRecordingPlaysWithNoChapters()
    {
        await using var feature = new PlayFeature();
        await feature.SegmentSettings.SaveAsync(SegmentSettings.LearningSwitched(true, RecordingFeature.Noon), CancellationToken.None);
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: HalfASecondSkipped);
        await feature.MarkedAsync(feature.Jobs.Jobs[0], new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(90), ChapterKind.Programme), new ChapterSegment(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(150), ChapterKind.Break), new ChapterSegment(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(300), ChapterKind.Programme));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Empty(plan.GetProperty("chapters").EnumerateArray());
    }

    [Fact(DisplayName = "BR-CD-013: while learning is off, an artefact of a recording not detected plays the chapters its encode marked, each break with something after it offered to be skipped")]
    public async Task WhileLearningIsOffTheMarkedChaptersArePlayed()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);
        await feature.MarkedAsync(
            feature.Jobs.Jobs[0],
            new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(90), ChapterKind.Programme),
            new ChapterSegment(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(150), ChapterKind.Break),
            new ChapterSegment(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(1740), ChapterKind.Programme),
            new ChapterSegment(TimeSpan.FromSeconds(1740), TimeSpan.FromSeconds(1799), ChapterKind.Break));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal(
            [(0d, 90d, "programme", false), (90d, 150d, "break", true), (150d, 1740d, "programme", false), (1740d, 1799d, "break", false)],
            plan.GetProperty("chapters").EnumerateArray().Select(Read));
    }

    [Fact(DisplayName = "BR-CS-002: a detection by a version no longer in use is not played")]
    public async Task ADetectionByAnotherVersionIsNotPlayed()
    {
        await using var feature = new PlayFeature();
        await feature.SegmentSettings.SaveAsync(SegmentSettings.LearningSwitched(true, RecordingFeature.Noon), CancellationToken.None);
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        await DetectedAsync(feature, recording.Id, (0, 300, SegmentKind.Programme, 0.98), (300, 360, SegmentKind.Break, 0.93), (360, 1800, SegmentKind.Programme, 0.99));
        feature.Versions.Active = HeldSegmentModelVersions.InUse(2, RecordingFeature.Noon);

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Empty(plan.GetProperty("chapters").EnumerateArray());
    }

    [Fact(DisplayName = "an artefact whose timeline was not kept has no detected stretch placed on it")]
    public async Task AnArtefactWithoutATimelineHasNoDetectedChapters()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);
        await DetectedAsync(feature, recording.Id, (0, 300, SegmentKind.Programme, 0.98), (300, 360, SegmentKind.Break, 0.93), (360, 1800, SegmentKind.Programme, 0.99));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal("direct", plan.GetProperty("route").GetString());
        Assert.Empty(plan.GetProperty("chapters").EnumerateArray());
    }

    [Fact(DisplayName = "BR-CD-001: the plan of an artefact says how far the recording's time stands ahead of the artefact's, the head the job that made it skipped, by which its chapters were moved")]
    public async Task TheArtefactsPlanSaysHowFarTheRecordingsTimeStandsAhead()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: Skipped(0.533));
        await DetectedAsync(feature, recording.Id, (0, 912.3, SegmentKind.Programme, 0.98), (912.3, 1002.3, SegmentKind.Break, 0.93), (1002.3, 1800, SegmentKind.Programme, 0.99));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal("direct", plan.GetProperty("route").GetString());
        Assert.Equal(0.533, plan.GetProperty("recordingOffsetSec").GetDouble());
        Assert.Equal([0d, 911.77d, 1001.77d], plan.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("startsAtSec").GetDouble()));
    }

    [Fact(DisplayName = "BR-CD-001: the plan says the head skipped by the artefact it hands over, not by a newer one it could not hand over, whatever chapters it carries")]
    public async Task ThePlanSaysTheHeadSkippedByTheArtefactItHandsOver()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: Skipped(0.533));
        feature.Encoded(recording, minutesLater: 10, onDisk: false, timeline: Skipped(1.2));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal("direct", plan.GetProperty("route").GetString());
        Assert.Equal(0.533, plan.GetProperty("recordingOffsetSec").GetDouble());
    }

    [Fact(DisplayName = "BR-CD-001: the plan of the recording itself says its time is the recording's own, whether or not an artefact was made of it")]
    public async Task TheRecordingsOwnPlanSaysItsTimeIsTheRecordingsOwn()
    {
        await using var feature = new PlayFeature();
        Recording alone = feature.Ended(RecordingOutcome.Complete);
        Recording encoded = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(encoded, timeline: Skipped(0.533));
        await DetectedAsync(feature, encoded.Id, (0, 300, SegmentKind.Programme, 0.98), (300, 360, SegmentKind.Break, 0.93), (360, 1800, SegmentKind.Programme, 0.99));

        JsonElement transcoded = await PlanAsync(feature, alone);
        JsonElement asRecorded = await PlanAsync(feature, encoded, "?source=recording");

        Assert.True(transcoded.GetProperty("transcodes").GetBoolean());
        Assert.Equal(0d, transcoded.GetProperty("recordingOffsetSec").GetDouble());
        Assert.Equal("recording", asRecorded.GetProperty("source").GetString());
        Assert.Equal(0d, asRecorded.GetProperty("recordingOffsetSec").GetDouble());
    }

    [Fact(DisplayName = "an artefact whose timeline was not kept says nothing of how far the recording's time stands ahead of its own")]
    public async Task AnArtefactWithoutATimelineSaysNoOffset()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal("direct", plan.GetProperty("route").GetString());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("recordingOffsetSec").ValueKind);
    }

    [Fact(DisplayName = "BR-CD-009: an answer that stands is played in place of the detection, a break it gives offered to be skipped whatever the threshold when something follows it")]
    public async Task AnAnswerIsPlayedInPlaceOfTheDetection()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: HalfASecondSkipped);
        await DetectedAsync(feature, recording.Id, (0, 300, SegmentKind.Programme, 0.98), (300, 360, SegmentKind.Break, 0.93), (360, 1800, SegmentKind.Programme, 0.99));
        await AnsweredAsync(feature, recording.Id, (0, 912.3, SegmentKind.Programme), (912.3, 1002.3, SegmentKind.Break), (1002.3, 1740, SegmentKind.Programme), (1740, 1800, SegmentKind.Break));

        JsonElement artefact = await PlanAsync(feature, recording);
        JsonElement original = await PlanAsync(feature, recording, "?source=recording");

        Assert.Equal(
            [(0d, 911.8d, "programme", false), (911.8d, 1001.8d, "break", true), (1001.8d, 1739.5d, "programme", false), (1739.5d, 1799.5d, "break", false)],
            artefact.GetProperty("chapters").EnumerateArray().Select(Read));
        Assert.Equal(
            [(0d, 912.3d, "programme", false), (912.3d, 1002.3d, "break", true), (1002.3d, 1740d, "programme", false), (1740d, 1800d, "break", false)],
            original.GetProperty("chapters").EnumerateArray().Select(Read));
    }

    [Fact(DisplayName = "BR-CD-013: while learning is off an answer is played on an artefact in place of the chapters its encode marked")]
    public async Task WhileLearningIsOffAnAnswerIsPlayedInPlaceOfTheMarkedChapters()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: HalfASecondSkipped);
        await feature.MarkedAsync(feature.Jobs.Jobs[0], new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(90), ChapterKind.Programme), new ChapterSegment(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(150), ChapterKind.Break), new ChapterSegment(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(1799.5), ChapterKind.Programme));
        await AnsweredAsync(feature, recording.Id, (0, 600.5, SegmentKind.Programme), (600.5, 660.5, SegmentKind.Break), (660.5, 1800, SegmentKind.Programme));

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal([0d, 600d, 660d], plan.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("startsAtSec").GetDouble()));
    }

    [Fact(DisplayName = "BR-CS-002: once the answer is withdrawn the plan carries the detection again")]
    public async Task OnceTheAnswerIsWithdrawnThePlanCarriesTheDetection()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        await DetectedAsync(feature, recording.Id, (0, 300, SegmentKind.Programme, 0.98), (300, 360, SegmentKind.Break, 0.93), (360, 1800, SegmentKind.Programme, 0.99));
        await AnsweredAsync(feature, recording.Id, (0, 1800, SegmentKind.Programme));
        JsonElement answered = await PlanAsync(feature, recording);

        await feature.Answers.WithdrawAsync(recording.Id, RecordingFeature.Noon.AddHours(3), CancellationToken.None);
        JsonElement withdrawn = await PlanAsync(feature, recording);

        Assert.Equal([(0d, 1800d, "programme", false)], answered.GetProperty("chapters").EnumerateArray().Select(Read));
        Assert.Equal([0d, 300d, 360d], withdrawn.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("startsAtSec").GetDouble()));
    }

    [Fact(DisplayName = "BR-CS-002: while learning is off, chapters an encode took from an answer since withdrawn are not played on the artefact")]
    public async Task ChaptersEncodedFromAWithdrawnAnswerAreNotPlayed()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        RecordingAnswer answer = await AnsweredAsync(feature, recording.Id, (0, 600.5, SegmentKind.Programme), (600.5, 660.5, SegmentKind.Break), (660.5, 1800, SegmentKind.Programme));
        EncodeChapterChoice choice = EncodeChapterChoice.Of(false, SegmentModelVersion.FirstNumber, SegmentStanding.Of(answer, null));
        feature.Encoded(recording, timeline: HalfASecondSkipped, chapters: choice.Reading(RecordingFeature.Noon.AddHours(2)));
        await feature.MarkedAsync(feature.Jobs.Jobs[0], [.. choice.Chapters(PlaybackFileClock.Of(HalfASecondSkipped))]);
        JsonElement answered = await PlanAsync(feature, recording);

        await feature.Answers.WithdrawAsync(recording.Id, RecordingFeature.Noon.AddHours(3), CancellationToken.None);
        JsonElement withdrawn = await PlanAsync(feature, recording);

        Assert.Equal([0d, 600d, 660d], answered.GetProperty("chapters").EnumerateArray().Select(chapter => chapter.GetProperty("startsAtSec").GetDouble()));
        Assert.Empty(withdrawn.GetProperty("chapters").EnumerateArray());
    }

    [Fact(DisplayName = "BR-CD-013: while learning is off, chapters the reader of the source marked are still played once an answer is withdrawn")]
    public async Task ChaptersTheReaderMarkedArePlayedOnceAnAnswerIsWithdrawn()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: HalfASecondSkipped, chapters: new ChapterReading(ChapterDetectorName.Ffmpeg, ChapterVerdict.Marked, 0.03, RecordingFeature.Noon.AddHours(1)));
        await feature.MarkedAsync(feature.Jobs.Jobs[0], new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(90), ChapterKind.Programme), new ChapterSegment(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(150), ChapterKind.Break), new ChapterSegment(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(1799.5), ChapterKind.Programme));
        await AnsweredAsync(feature, recording.Id, (0, 1800, SegmentKind.Programme));
        await feature.Answers.WithdrawAsync(recording.Id, RecordingFeature.Noon.AddHours(3), CancellationToken.None);

        JsonElement plan = await PlanAsync(feature, recording);

        Assert.Equal(
            [(0d, 90d, "programme", false), (90d, 150d, "break", true), (150d, 1799.5d, "programme", false)],
            plan.GetProperty("chapters").EnumerateArray().Select(Read));
    }

    [Fact(DisplayName = "BR-CA-001: asking for the plan writes nothing: no detection is kept and no setting saved")]
    public async Task AskingForThePlanWritesNothing()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, timeline: HalfASecondSkipped);
        await DetectedAsync(feature, recording.Id, (0, 300, SegmentKind.Programme, 0.98), (300, 360, SegmentKind.Break, 0.93), (360, 1800, SegmentKind.Programme, 0.99));
        int kept = feature.Detections.Keeps;

        await PlanAsync(feature, recording);
        await PlanAsync(feature, recording, "?source=recording");

        Assert.Equal(kept, feature.Detections.Keeps);
        Assert.Equal(0, feature.SegmentSettings.Saves);
        Assert.Equal(0, feature.Answers.Writes);
    }

    private static EncodeTimeline Skipped(double head)
        => new(TimeSpan.FromSeconds(1.4), TimeSpan.FromSeconds(head), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(head));

    private static async Task<JsonElement> PlanAsync(PlayFeature feature, Recording recording, string query = "")
        => (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording, query))).GetProperty("data");

    private static (double Starts, double Ends, string Kind, bool Skippable) Read(JsonElement chapter)
        => (chapter.GetProperty("startsAtSec").GetDouble(),
            chapter.GetProperty("endsAtSec").GetDouble(),
            chapter.GetProperty("kind").GetString()!,
            chapter.GetProperty("skippable").GetBoolean());

    private static async Task<RecordingAnswer> AnsweredAsync(PlayFeature feature, RecordingId recording, params (double Starts, double Ends, SegmentKind Kind)[] stretches)
    {
        RecordingAnswer answer = RecordingAnswer.Given(
            recording,
            AnsweredStretches.Read([.. stretches.Select(stretch => new SentStretch(stretch.Starts, stretch.Ends, stretch.Kind))], TimeSpan.FromMinutes(30)).Stretches!,
            true,
            RecordingFeature.Noon.AddHours(2));

        await feature.Answers.AnswerAsync(answer, CancellationToken.None);

        return answer;
    }

    private static async Task DetectedAsync(PlayFeature feature, RecordingId recording, params (double Starts, double Ends, SegmentKind Kind, double Probability)[] stretches)
    {
        SegmentSpan[] segments = [.. stretches.Select(stretch => new SegmentSpan(TimeSpan.FromSeconds(stretch.Starts), TimeSpan.FromSeconds(stretch.Ends), stretch.Kind, stretch.Probability))];

        await feature.Detections.KeepAsync(
            RecordingDetection.Of(recording, SegmentModelVersion.FirstNumber, SegmentDetection.Judged(segments, TimeSpan.FromMinutes(30), []), 2, RecordingFeature.Noon, RecordingFeature.Noon),
            CancellationToken.None);
    }
}
