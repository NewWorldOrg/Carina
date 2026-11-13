using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;

using Carina.Domain.Base;
using Carina.Domain.Captions;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Machines;
using Carina.Infrastructure.Tests.Fixtures.Encodings;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Unit.Infra.Encodings;

[Trait("Category", "Unit")]
public sealed class EncodeJobRunnerTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const string WritesTheWorkFileAndReportsProgress = """
        printf 'out_time_us=0\nspeed=1.0x\nprogress=continue\n'
        printf 'out_time_us=5000000\nspeed=2.0x\nprogress=continue\n'
        printf 'the picture' > "$destination"
        printf 'out_time_us=10000000\nspeed=2.0x\nprogress=end\n'
        """;

    /// <summary>
    /// The same run, made to outlive the look at it: the first thing it writes is more than a pipe
    /// holds, so it cannot exit before it has been identified and written into the ledger.
    /// </summary>
    private const string OutlivesTheLookAndReportsProgress = """
        head -c 70000 /dev/zero | tr '\0' '.'
        printf '\n'
        printf 'out_time_us=0\nspeed=1.0x\nprogress=continue\n'
        printf 'out_time_us=5000000\nspeed=2.0x\nprogress=continue\n'
        printf 'the picture' > "$destination"
        printf 'out_time_us=10000000\nspeed=2.0x\nprogress=end\n'
        """;

    private static readonly TimeSpan Skipped = TimeSpan.FromSeconds(0.5);

    private static readonly TimeSpan Artefact = EncodeHarness.Whole - Skipped;

    private const string Larger = "a picture larger than its recording";

    private const string StillLarger = "a smaller picture, still large";

    private const string Small = "small";

    private static readonly long TwoSecondsIn = (long)((MeasuredHeads.Start.TotalSeconds + 2) * 90_000);

    private const string CarriedWithout =
        """{"streams":[{"codec_name":"h264","codec_type":"video","disposition":{"default":1}},{"codec_name":"aac","codec_type":"audio","disposition":{"default":1}}],"format":{"duration":"10.000000"}}""";

    private const string CarriedWithTheTrack =
        """{"streams":[{"codec_name":"h264","codec_type":"video","disposition":{"default":1}},{"codec_name":"aac","codec_type":"audio","disposition":{"default":1}},{"codec_name":"mov_text","codec_type":"subtitle","disposition":{"default":0},"tags":{"language":"jpn"}}],"format":{"duration":"10.000000"}}""";

    private const string Source = "<the recording>";

    private const string Work = "<the work file>";

    private const string Metadata = "<the chapters>";

    [Fact(DisplayName = "a job is run to its end: the programme writes the work file the ledger was told about, the artefact is placed by the ledger, and the job completes")]
    public async Task AJobIsRunToItsEnd()
    {
        using var harness = new EncodeHarness();
        harness.Standing(WritesTheWorkFileAndReportsProgress);
        Recording recording = harness.Recorded();
        EncodeProfile profile = harness.Defined();
        EncodeJob job = harness.Running(recording.Id, profile.Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(EncodeJobStatus.Completed, job.Status);
        Assert.Equal("the picture", File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.False(File.Exists(harness.WorkPathOf(job)));
        Assert.Equal(EncodeHarness.Broadcast, File.ReadAllText(harness.SourcePathOf(recording)));
        Assert.Equal([$"recorded {job.WorkFileName.Value}"], harness.Scratch.Moves.Where(move => move.StartsWith("recorded", StringComparison.Ordinal)));
        Assert.Equal(EncodeScratchFate.BecameTheArtefact, Assert.Single(harness.Scratch.Files).Fate);
        Assert.Equal([harness.SourcePathOf(recording), harness.WorkPathOf(job)], harness.Lengths.Asked);
        Assert.Equal([(harness.SourcePathOf(recording), recording.ServiceId)], harness.Heads.Asked);
    }

    [Fact(DisplayName = "a job asked to make it again under another profile removes what the earlier job made once its own artefact is placed, and the recording is left as it was")]
    public async Task AJobMadeAgainRemovesWhatTheEarlierJobMadeOnceItsOwnIsPlaced()
    {
        using EncodeHarness harness = new();
        harness.Standing(WritesTheWorkFileAndReportsProgress);
        Recording recording = harness.Recorded();
        EncodeJob earlier = harness.Made(recording.Id, harness.Defined().Id);
        File.WriteAllText(harness.ArtefactPathOf(earlier), "the first picture");
        EncodeJob again = harness.RunningAgain(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(again, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal("the picture", File.ReadAllText(harness.ArtefactPathOf(again)));
        Assert.False(File.Exists(harness.ArtefactPathOf(earlier)));
        Assert.Equal(EncodeHarness.Broadcast, File.ReadAllText(harness.SourcePathOf(recording)));
        Assert.NotNull(earlier.ReplacedAt);
        Assert.True(again.StandsAsTheArtefact);
    }

    [Fact(DisplayName = "a job asked to make it again that fails leaves what the earlier job made exactly where it was, still the recording's artefact")]
    public async Task AJobMadeAgainThatFailsLeavesTheEarlierArtefactAsItWas()
    {
        using EncodeHarness harness = new();
        harness.Standing("""
            printf 'garbage' > "$destination"
            exit 187
            """);
        Recording recording = harness.Recorded();
        EncodeJob earlier = harness.Made(recording.Id, harness.Defined().Id);
        File.WriteAllText(harness.ArtefactPathOf(earlier), "the first picture");
        EncodeJob again = harness.RunningAgain(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(again, Cancel);

        Assert.Equal(EncodeJobStatus.Failed, ended);
        Assert.Equal("the first picture", File.ReadAllText(harness.ArtefactPathOf(earlier)));
        Assert.Null(earlier.ReplacedAt);
        Assert.True(earlier.StandsAsTheArtefact);
        Assert.DoesNotContain(harness.Scratch.Files, scratch => scratch.Kind is EncodeScratchKind.ReplacedArtefact);
    }

    [Fact(DisplayName = "the head skip read off the source is the one -ss the programme is handed, after the input, and the job keeps it beside the source's start as the shift a caption takes")]
    public async Task TheHeadSkipReadOffTheSourceIsTheOneSsTheProgrammeIsHanded()
    {
        using var harness = new EncodeHarness();
        string arguments = harness.Room.Under("arguments");
        harness.Standing($"printf '%s\\n' \"$@\" > \"{arguments}\"; printf 'the picture' > \"$destination\"");
        harness.Heads.Reading = SourceHeadReading.Read(TimeSpan.FromSeconds(30499.474078), TimeSpan.FromSeconds(30499.981278));
        harness.Lengths.Reading = SourceLengthReading.Read(TimeSpan.FromSeconds(2097.502489));
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        string[] handed = File.ReadAllLines(arguments);
        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(1, handed.Count(argument => argument == "-ss"));
        Assert.Equal("0.5072", handed[Array.IndexOf(handed, "-ss") + 1]);
        Assert.True(Array.IndexOf(handed, "-ss") > Array.IndexOf(handed, "-i"), "the skip is a trim after the input");
        Assert.Equal(TimeSpan.FromSeconds(0.5072), job.Timeline!.HeadSkip);
        Assert.Equal(TimeSpan.FromSeconds(30499.474078), job.Timeline.SourceStart);
        Assert.Equal(TimeSpan.FromSeconds(30499.981278), job.Timeline.CaptionShift);
        Assert.Equal(TimeSpan.FromSeconds(2097.502489), job.Timeline.SourceLength);
        Assert.Contains(harness.RunnerLog.Said, line => line.Contains("skipping 0.5072 s of head", StringComparison.Ordinal) && line.Contains("30499.981278 s on the source's clock", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "the whole the job measures its headway against is what the source has left after the head skip, not the source entire")]
    public async Task TheWholeIsWhatTheSourceHasLeftAfterTheHeadSkip()
    {
        using var harness = new EncodeHarness();
        harness.Standing(WritesTheWorkFileAndReportsProgress);
        harness.Heads.Reading = SourceHeadReading.Read(MeasuredHeads.Start, MeasuredHeads.Start + TimeSpan.FromSeconds(2));
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        await harness.Runner.RunAsync(job, Cancel);

        string[] told = [.. harness.RunnerLog.Said.Where(line => line.Contains("of the way through", StringComparison.Ordinal))];
        Assert.Contains(" 62% of the way through", told[1], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a first picture further than five seconds into the source fails the job as head too far, with the number, and the programme is never started")]
    public async Task AFirstPictureFurtherThanFiveSecondsInFailsAsHeadTooFar()
    {
        using var harness = new EncodeHarness();
        string marker = harness.Room.Under("it-ran");
        harness.Standing($"printf ran > \"{marker}\"");
        harness.Heads.Reading = SourceHeadReading.Read(MeasuredHeads.Start, MeasuredHeads.Start + TimeSpan.FromSeconds(5.5));
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Failed, ended);
        Assert.Equal(EncodeFailure.HeadTooFar, job.Failure!.Failure);
        Assert.Contains("5.5 s into the source", job.Failure.Note, StringComparison.Ordinal);
        Assert.False(File.Exists(marker));
        Assert.Empty(harness.Scratch.Files);
        Assert.Null(job.Timeline);
    }

    [Fact(DisplayName = "a head with no picture to read is head too far, a head the prober refused is the programme's own refusal, and a head the prober could not be asked about is capability unavailable; none of them runs the programme")]
    public async Task AHeadThatCannotBeReadFailsBeforeTheProgrammeStarts()
    {
        using var harness = new EncodeHarness();
        string marker = harness.Room.Under("it-ran");
        harness.Standing($"printf ran > \"{marker}\"");
        EncodeProfile profile = harness.Defined();

        harness.Heads.Reading = SourceHeadReading.Unanswered(SourceHeadFault.SaidNothing, "decoded no picture in the first 6 s");
        EncodeJob quiet = harness.Running(harness.Recorded().Id, profile.Id);
        Assert.Equal(EncodeJobStatus.Failed, await harness.Runner.RunAsync(quiet, Cancel));
        Assert.Equal(EncodeFailure.HeadTooFar, quiet.Failure!.Failure);
        Assert.Contains("decoded no picture", quiet.Failure.Note, StringComparison.Ordinal);

        harness.Heads.Reading = SourceHeadReading.Unanswered(SourceHeadFault.ProgrammeMissing, "no such programme");
        EncodeJob unasked = harness.Running(harness.Recorded().Id, profile.Id);
        Assert.Equal(EncodeJobStatus.Failed, await harness.Runner.RunAsync(unasked, Cancel));
        Assert.Equal(EncodeFailure.CapabilityUnavailable, unasked.Failure!.Failure);

        harness.Heads.Reading = SourceHeadReading.Refused(1, "Invalid data found when processing input");
        EncodeJob refused = harness.Running(harness.Recorded().Id, profile.Id);
        Assert.Equal(EncodeJobStatus.Failed, await harness.Runner.RunAsync(refused, Cancel));
        Assert.Equal(EncodeFailure.FfmpegExitedNonZero, refused.Failure!.Failure);
        Assert.Contains("the programme exited 1 while reading the head", refused.Failure.Note, StringComparison.Ordinal);

        Assert.False(File.Exists(marker));
    }

    [Fact(DisplayName = "the artefact is measured before it is placed and its length kept beside the source's; one that came out further than a second from what the source had left is a note on a completed job, not a failure")]
    public async Task TheArtefactIsMeasuredAndADisagreementIsANoteNotAFailure()
    {
        using var harness = new EncodeHarness();
        harness.Standing(WritesTheWorkFileAndReportsProgress);
        harness.Heads.Reading = SourceHeadReading.Read(MeasuredHeads.Start, MeasuredHeads.Start + TimeSpan.FromSeconds(0.5));
        Recording recording = harness.Recorded();
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);
        harness.Lengths.ByPath = path => path == harness.SourcePathOf(recording)
            ? SourceLengthReading.Read(TimeSpan.FromSeconds(10))
            : SourceLengthReading.Read(TimeSpan.FromSeconds(12));

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(TimeSpan.FromSeconds(12), job.Timeline!.ArtefactLength);
        Assert.Equal(TimeSpan.FromSeconds(2.5), job.Timeline.Drift);
        Assert.False(job.Timeline.LengthsAgree);
        Assert.Null(job.Failure);
        Assert.Contains(harness.RunnerLog.Said, line => line.Contains("2.5 s apart", StringComparison.Ordinal) && line.Contains("The job completes", StringComparison.Ordinal));
        Assert.True(File.Exists(harness.ArtefactPathOf(job)));
    }

    [Fact(DisplayName = "an artefact whose length cannot be measured completes with its clock unchecked, and says so")]
    public async Task AnArtefactThatCannotBeMeasuredCompletesUnchecked()
    {
        using var harness = new EncodeHarness();
        harness.Standing(WritesTheWorkFileAndReportsProgress);
        Recording recording = harness.Recorded();
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);
        harness.Lengths.ByPath = path => path == harness.SourcePathOf(recording)
            ? null
            : SourceLengthReading.Unanswered(SourceLengthFault.SaidNothing, "duration=N/A");

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Null(job.Timeline!.ArtefactLength);
        Assert.Null(job.Timeline.LengthsAgree);
        Assert.Contains(harness.RunnerLog.Said, line => line.Contains("could not be measured", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "how far the job has got is told from 0 to 100 as the programme reports it")]
    public async Task HowFarTheJobHasGotIsToldFromNoughtToAHundred()
    {
        using var harness = new EncodeHarness();
        harness.Standing(WritesTheWorkFileAndReportsProgress);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        await harness.Runner.RunAsync(job, Cancel);

        string[] told = [.. harness.RunnerLog.Said.Where(line => line.Contains("of the way through", StringComparison.Ordinal))];
        Assert.Equal(3, told.Length);
        Assert.Contains(" 0% of the way through", told[0], StringComparison.Ordinal);
        Assert.Contains(" 50% of the way through", told[1], StringComparison.Ordinal);
        Assert.Contains(" 100% of the way through", told[2], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "the work file is written into the ledger before the programme is started, and only then does the programme get its path")]
    public async Task TheWorkFileIsInTheLedgerBeforeTheProgrammeStarts()
    {
        using var harness = new EncodeHarness();
        string marker = harness.Room.Under("it-ran");
        harness.Standing($"printf ran > \"{marker}\"; printf 'the picture' > \"$destination\"");
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);
        bool nothingHadRunWhenRecorded = false;
        harness.Scratch.WhenRecording = _ => nothingHadRunWhenRecorded = !File.Exists(marker);

        await harness.Runner.RunAsync(job, Cancel);

        Assert.True(File.Exists(marker), "the programme ran");
        Assert.True(nothingHadRunWhenRecorded, "the ledger was written while the programme had not yet run");
        Assert.Equal(job.WorkFileName, Assert.Single(harness.Scratch.Files).FileName);
    }

    [Fact(DisplayName = "a recording that is not where the ledger says fails as source missing, and the programme is never started")]
    public async Task ARecordingNotWhereTheLedgerSaysIsSourceMissing()
    {
        using var harness = new EncodeHarness();
        string marker = harness.Room.Under("it-ran");
        harness.Standing($"printf ran > \"{marker}\"");
        Recording recording = harness.Recorded();
        File.Delete(harness.SourcePathOf(recording));
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Failed, ended);
        Assert.Equal(EncodeFailure.SourceMissing, job.Failure!.Failure);
        Assert.False(File.Exists(marker));
        Assert.Empty(harness.Scratch.Files);
        Assert.DoesNotContain(harness.Room.Root, job.Failure.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a recording file that holds nothing is source missing too, and a recording the ledger does not hold at all")]
    public async Task ARecordingThatHoldsNothingIsSourceMissingToo()
    {
        using var harness = new EncodeHarness();
        harness.Standing("printf 'the picture' > \"$destination\"");
        Recording emptied = harness.Recorded();
        File.WriteAllText(harness.SourcePathOf(emptied), string.Empty);
        EncodeJob empty = harness.Running(emptied.Id, harness.Defined().Id);
        EncodeJob unknown = harness.Running(RecordingId.New(), harness.Defined().Id);

        Assert.Equal(EncodeJobStatus.Failed, await harness.Runner.RunAsync(empty, Cancel));
        Assert.Equal(EncodeFailure.SourceMissing, empty.Failure!.Failure);
        Assert.Contains("holds nothing", empty.Failure.Note, StringComparison.Ordinal);

        Assert.Equal(EncodeJobStatus.Failed, await harness.Runner.RunAsync(unknown, Cancel));
        Assert.Equal(EncodeFailure.SourceMissing, unknown.Failure!.Failure);
    }

    [Fact(DisplayName = "a programme that exits non-zero fails the job as such, with the tail of what it said beside the classification and no path in it, and the work file is swept by the ledger")]
    public async Task AProgrammeThatExitsNonZeroFailsTheJobWithWhatItSaid()
    {
        using var harness = new EncodeHarness();
        harness.Standing("""
            printf 'garbage' > "$destination"
            echo "$destination: Invalid data found when processing input" >&2
            exit 187
            """);
        Recording recording = harness.Recorded();
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Failed, ended);
        Assert.Equal(EncodeFailure.FfmpegExitedNonZero, job.Failure!.Failure);
        Assert.Contains("exited 187", job.Failure.Note, StringComparison.Ordinal);
        Assert.Contains("Invalid data found when processing input", job.Failure.Note, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Room.Root, job.Failure.Note, StringComparison.Ordinal);
        Assert.DoesNotContain(job.WorkFileName.Value, job.Failure.Note, StringComparison.Ordinal);
        Assert.False(File.Exists(harness.WorkPathOf(job)), "the garbage the programme left is swept by the ledger");
        Assert.False(File.Exists(harness.ArtefactPathOf(job)));
        Assert.Equal(EncodeScratchFate.Removed, Assert.Single(harness.Scratch.Files).Fate);
        Assert.True(File.Exists(harness.SourcePathOf(recording)), "the recording is never touched");
    }

    [Fact(DisplayName = "a programme that ran out of room is told apart from one that refused")]
    public async Task AProgrammeThatRanOutOfRoomIsToldApartFromOneThatRefused()
    {
        using var harness = new EncodeHarness();
        harness.Standing("""
            echo "av_interleaved_write_frame(): No space left on device" >&2
            exit 1
            """);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeFailure.NotEnoughRoom, job.Failure!.Failure);
    }

    [Fact(DisplayName = "a programme that stops reporting progress is stopped where it stands and the job fails as timed out")]
    public async Task AProgrammeThatStopsReportingProgressIsStoppedAndTheJobTimesOut()
    {
        using var harness = new EncodeHarness();
        harness.Settings = harness.Settings with { StalledAfter = TimeSpan.FromMilliseconds(400) };
        harness.Clock = TimeProvider.System;
        string marker = harness.Room.Under("still-alive");
        harness.Standing($"""
            printf 'out_time_us=0\nprogress=continue\n'
            sleep 30
            printf alive > "{marker}"
            """);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        Stopwatch waited = Stopwatch.StartNew();
        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Failed, ended);
        Assert.Equal(EncodeFailure.TimedOut, job.Failure!.Failure);
        Assert.Contains("no headway was made for", job.Failure.Note, StringComparison.Ordinal);
        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(15), $"the programme was stopped rather than waited for: {waited.Elapsed}");
        Assert.False(File.Exists(marker));
    }

    [Fact(DisplayName = "a codec this machine cannot encode anywhere fails the job as capability unavailable, and the programme is never started")]
    public async Task ACodecThisMachineCannotEncodeAnywhereIsCapabilityUnavailable()
    {
        using var harness = new EncodeHarness();
        string marker = harness.Room.Under("it-ran");
        harness.Standing($"printf ran > \"{marker}\"");
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined(EncodeCodec.H265).Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Failed, ended);
        Assert.Equal(EncodeFailure.CapabilityUnavailable, job.Failure!.Failure);
        Assert.False(File.Exists(marker));
        Assert.Empty(harness.Scratch.Files);
    }

    [Fact(DisplayName = "a card asked for and out of reach degrades to the processor, the run is written down as degraded, and the job still completes")]
    public async Task ACardAskedForAndOutOfReachDegradesToTheProcessor()
    {
        using var harness = new EncodeHarness();
        harness.Settings = harness.Settings with { Prefer = EncodeEncoder.Vaapi };
        string arguments = harness.Room.Under("arguments");
        harness.Standing($"printf '%s\\n' \"$@\" > \"{arguments}\"; printf 'the picture' > \"$destination\"");
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        string[] handed = File.ReadAllLines(arguments);
        Assert.Contains("libx264", handed);
        Assert.DoesNotContain("h264_vaapi", handed);
        Assert.DoesNotContain("-vaapi_device", handed);
        Assert.Contains(harness.RunnerLog.Warnings, line => line.Contains("TheCardIsOutOfReach", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "a run on the card opens it through the render node the machine is set to")]
    public async Task ARunOnTheCardOpensItThroughTheRenderNodeTheMachineIsSetTo()
    {
        using var harness = new EncodeHarness();
        harness.Settings = harness.Settings with { Prefer = EncodeEncoder.Vaapi };
        harness.Machine.Can = MachineCapabilities.Of(
            CardStanding.Usable,
            [Faculty.EncodeH264OnTheProcessor, Faculty.EncodeH264OnTheCard, Faculty.DecodeAribCaptions],
            "the card answered");
        string arguments = harness.Room.Under("arguments");
        harness.Standing($"printf '%s\\n' \"$@\" > \"{arguments}\"; printf 'the picture' > \"$destination\"");
        harness.Programmes = harness.Programmes with { RenderNode = "/dev/dri/renderD129" };
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        string[] handed = File.ReadAllLines(arguments);
        Assert.Contains("h264_vaapi", handed);
        Assert.Equal("/dev/dri/renderD129", handed[Array.IndexOf(handed, "-vaapi_device") + 1]);
    }

    [Fact(DisplayName = "the programme is handed the recording as one argument and the work file as the last, and nothing a broadcaster wrote")]
    public async Task TheProgrammeIsHandedTheRecordingAndTheWorkFileAsArguments()
    {
        using var harness = new EncodeHarness();
        string arguments = harness.Room.Under("arguments");
        harness.Standing($"printf '%s\\n' \"$@\" > \"{arguments}\"; printf 'the picture' > \"$destination\"");
        Recording recording = harness.Recorded();
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        await harness.Runner.RunAsync(job, Cancel);

        string[] handed = File.ReadAllLines(arguments);
        Assert.Contains(harness.SourcePathOf(recording), handed);
        Assert.Equal(harness.WorkPathOf(job), handed[^1]);
        Assert.Contains("p:1064:v:0", handed);
        Assert.DoesNotContain(handed, argument => argument.Contains("A programme", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "a recording of a broadcast that put two languages on one sound is encoded with the main language in both ears")]
    public async Task ARecordingOfTwoLanguagesOnOneSoundIsEncodedWithTheMainLanguageInBothEars()
    {
        using var harness = new EncodeHarness();
        string arguments = harness.Room.Under("arguments");
        harness.Standing($"printf '%s\\n' \"$@\" > \"{arguments}\"; printf 'the picture' > \"$destination\"");
        Recording recording = harness.Recorded(audio: AudioMode.DualMono, sounds: 1);
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        string[] handed = File.ReadAllLines(arguments);
        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Contains("pan=stereo|c0=c0|c1=c0", handed);
        Assert.Contains("p:1064:a:0", handed);
        Assert.Contains("aac", handed);
        Assert.DoesNotContain("copy", handed);
        Assert.DoesNotContain("aac_adtstoasc", handed);
    }

    [Fact(DisplayName = "a recording whose broadcast announced nothing about its sound is encoded with every sound copied over as it stands")]
    public async Task ARecordingThatAnnouncedNothingIsEncodedWithEverySoundCopiedOver()
    {
        using var harness = new EncodeHarness();
        string arguments = harness.Room.Under("arguments");
        harness.Standing($"printf '%s\\n' \"$@\" > \"{arguments}\"; printf 'the picture' > \"$destination\"");
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        await harness.Runner.RunAsync(job, Cancel);

        string[] handed = File.ReadAllLines(arguments);
        Assert.Contains("p:1064:a", handed);
        Assert.Contains("copy", handed);
        Assert.Contains("aac_adtstoasc", handed);
        Assert.DoesNotContain(handed, argument => argument.StartsWith("pan=", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "a stop asked for while the programme runs stops the programme and leaves the job running in the ledger, programme and all, for the next start to put back")]
    public async Task AStopWhileTheProgrammeRunsLeavesTheJobRunningInTheLedger()
    {
        using var harness = new EncodeHarness();
        string began = harness.Room.Under("began");
        string finished = harness.Room.Under("finished");
        harness.Standing($"""
            printf 'out_time_us=0\nprogress=continue\n'
            printf began > "{began}"
            sleep 30
            printf finished > "{finished}"
            """);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);
        using var stopping = new CancellationTokenSource();

        Task<EncodeJobStatus> running = harness.Runner.RunAsync(job, stopping.Token);
        await Eventually.Happens(() => File.Exists(began), "the programme began");
        Stopwatch waited = Stopwatch.StartNew();
        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(15), $"the programme was stopped rather than waited for: {waited.Elapsed}");
        Assert.Equal(EncodeJobStatus.Running, job.Status);
        Assert.Null(job.Failure);
        Assert.NotNull(job.Programme);
        Assert.All(
            harness.Jobs.Moves.Where(move => move.StartsWith("saved", StringComparison.Ordinal)),
            move => Assert.EndsWith(" Running", move, StringComparison.Ordinal));
        Assert.True(Assert.Single(harness.Scratch.Files).IsOwedARemoval, "the work file is left for the next start, never swept while the ledger says the job runs");
        Assert.False(File.Exists(finished));
    }

    [Fact(DisplayName = "a job whose source is under a root this process cannot place is refused without touching anything")]
    public async Task AJobWhoseSourceIsUnderARootOutOfReachIsRefused()
    {
        using var harness = new EncodeHarness();
        harness.Standing("printf 'the picture' > \"$destination\"");
        Recording elsewhere = harness.Recorded(root: new OutputRoot("bulk"));
        EncodeJob job = harness.Running(elsewhere.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Failed, ended);
        Assert.Equal(EncodeFailure.CapabilityUnavailable, job.Failure!.Failure);
        Assert.Contains("'bulk'", job.Failure.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "only a job the ledger holds as running is run")]
    public async Task OnlyARunningJobIsRun()
    {
        using var harness = new EncodeHarness();
        EncodeJob waiting = EncodeJob.Queue(EncodeJobId.New(), RecordingId.New(), EncodeProfileId.New(), EncodeDestinationId.New(), EncodeHarness.Primary, EncodeHarness.Queued);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Runner.RunAsync(waiting, Cancel));
    }

    [Fact(DisplayName = "the programme's id and start are written into the ledger before it has reported anything, and let go of once the job has ended")]
    public async Task TheProgrammesIdAndStartAreWrittenIntoTheLedgerBeforeItReportsAnything()
    {
        using var harness = new EncodeHarness();
        harness.Standing(OutlivesTheLookAndReportsProgress);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);
        List<(RunningProgramme? Programme, EncodeHeadway? Headway, EncodeJobStatus Status)> saved = [];
        harness.Jobs.WhenSaving = saving => saved.Add((saving.Programme, saving.Headway, saving.Status));

        await harness.Runner.RunAsync(job, Cancel);

        (RunningProgramme? programme, EncodeHeadway? headway, EncodeJobStatus status) =
            saved.First(save => save.Programme is not null);
        Assert.Equal(EncodeJobStatus.Running, status);
        Assert.NotNull(programme);
        Assert.Null(headway);
        Assert.All(saved.TakeWhile(save => save.Programme is null), save => Assert.Null(save.Headway));
        Assert.InRange(programme.ProcessId, 2, int.MaxValue);
        Assert.InRange(programme.StartedAt, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddSeconds(2));
        Assert.Null(job.Programme);
        Assert.Equal(EncodeJobStatus.Completed, job.Status);
    }

    [Fact(DisplayName = "where the run went is written on the job — asked for the card, ran on the processor — and stays written once it has ended")]
    public async Task WhereTheRunWentIsWrittenOnTheJob()
    {
        using var harness = new EncodeHarness();
        harness.Settings = harness.Settings with { Prefer = EncodeEncoder.Vaapi };
        harness.Standing("printf 'the picture' > \"$destination\"");
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, job.Status);
        Assert.Equal(new EncodeRoute(EncodeEncoder.Vaapi, EncodeEncoder.Software, EncodeSwerve.TheCardIsOutOfReach), job.Route);
        Assert.Contains(harness.Jobs.Moves, move => move.StartsWith("saved", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "a run that went where it was sent is written down as such, with no swerve")]
    public async Task ARunThatWentWhereItWasSentIsWrittenDownAsSuch()
    {
        using var harness = new EncodeHarness();
        harness.Standing("printf 'the picture' > \"$destination\"");
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(new EncodeRoute(EncodeEncoder.Software, EncodeEncoder.Software, null), job.Route);
    }

    [Fact(DisplayName = "headway is written into the ledger as the programme reports it — the portion, what is left and when — and the last of it stays with the job that ended")]
    public async Task HeadwayIsWrittenIntoTheLedgerAsTheProgrammeReportsIt()
    {
        using var harness = new EncodeHarness();
        harness.Standing(WritesTheWorkFileAndReportsProgress);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);
        List<EncodeHeadway> saved = [];
        harness.Jobs.WhenSaving = saving =>
        {
            if (saving.Headway is { } headway && saving.Status is EncodeJobStatus.Running)
            {
                saved.Add(headway);
            }
        };

        await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal([0, 0.5, 1], saved.Select(headway => headway.Portion));
        Assert.Equal(TimeSpan.FromSeconds(2.5), saved[1].Left);
        Assert.All(saved, headway => Assert.Equal(harness.Clock.GetUtcNow().UtcDateTime, headway.At));
        Assert.Equal(1, job.Headway!.Portion);
        Assert.Equal(TimeSpan.Zero, job.Headway.Left);
    }

    [Fact(DisplayName = "a programme that reports often is written into the ledger at every tenth and at least every heartbeat, not at every report")]
    public async Task AProgrammeThatReportsOftenIsWrittenAtEveryTenthAndEveryHeartbeat()
    {
        using var harness = new EncodeHarness();
        var clock = new HandTurnedClock(new DateTimeOffset(2026, 9, 5, 4, 0, 0, TimeSpan.Zero));
        harness.Clock = clock;
        harness.Standing("""
            for step in 1 2 3 4 5 6 7 8 9 10 11 12; do
                printf 'out_time_us=%d\nspeed=1.0x\nprogress=continue\n' "$((step * 50000))"
            done
            printf 'the picture' > "$destination"
            printf 'out_time_us=10000000\nspeed=1.0x\nprogress=end\n'
            """);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);
        int heartbeats = 0;
        harness.Jobs.WhenSaving = saving =>
        {
            if (saving.Status is EncodeJobStatus.Running && saving.Headway is not null)
            {
                heartbeats++;
                clock.Turn(EncodeJobRunner.HeartbeatEvery / 4);
            }
        };

        await harness.Runner.RunAsync(job, Cancel);

        Assert.InRange(heartbeats, 2, 5);
    }

    [Fact(DisplayName = "the programme is handed the cap the ledger holds, and no more cores than this machine has")]
    public async Task TheProgrammeIsHandedTheCoreCap()
    {
        using var harness = new EncodeHarness();
        harness.Settings = harness.Settings with { MostCores = 6 };
        harness.AutoRun.Standing = new EncodeAutoRunStanding(true, 1, true, EncodeHarness.Queued);
        string arguments = harness.Room.Under("arguments");
        harness.Standing($"printf '%s\\n' \"$@\" > \"{arguments}\"; printf 'the picture' > \"$destination\"");
        harness.Programmes = harness.Programmes with { Cores = 4 };
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        await harness.Runner.RunAsync(job, Cancel);

        string[] handed = File.ReadAllLines(arguments);
        Assert.Equal(2, handed.Count(argument => argument == "-threads"));
        Assert.Equal("1", handed[Array.IndexOf(handed, "-threads") + 1]);
        Assert.Equal("1", handed[Array.IndexOf(handed, "-filter_threads") + 1]);

        harness.AutoRun.Standing = harness.AutoRun.Standing with { MostCores = EncodeAutoRun.MostCoresAnyMachineHas };
        await harness.Runner.RunAsync(harness.Running(harness.Recorded().Id, harness.Defined().Id), Cancel);
        handed = File.ReadAllLines(arguments);
        Assert.Equal("4", handed[Array.IndexOf(handed, "-threads") + 1]);
    }

    [Fact(DisplayName = "a machine nobody has settled runs on the cap it was deployed with")]
    public async Task AMachineNobodyHasSettledRunsOnTheCapItWasDeployedWith()
    {
        using var harness = new EncodeHarness();
        harness.Settings = harness.Settings with { MostCores = 3 };
        string arguments = harness.Room.Under("arguments");
        harness.Standing($"printf '%s\\n' \"$@\" > \"{arguments}\"; printf 'the picture' > \"$destination\"");
        harness.Programmes = harness.Programmes with { Cores = 8 };
        harness.AutoRun.Standing = EncodeAutoRunStanding.Over(null, harness.Settings, harness.Programmes);

        await harness.Runner.RunAsync(harness.Running(harness.Recorded().Id, harness.Defined().Id), Cancel);

        string[] handed = File.ReadAllLines(arguments);
        Assert.Equal("3", handed[Array.IndexOf(handed, "-threads") + 1]);
    }

    [Fact(DisplayName = "a programme that is not on this machine fails the job as capability unavailable rather than blaming the recording")]
    public async Task AProgrammeNotOnThisMachineIsCapabilityUnavailable()
    {
        using var harness = new EncodeHarness();
        harness.Programmes = new MachineSettings { Programme = harness.Room.Under("no-such-programme") };
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Failed, ended);
        Assert.Equal(EncodeFailure.CapabilityUnavailable, job.Failure!.Failure);
        Assert.DoesNotContain(harness.Room.Root, job.Failure.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a detection that marked something is written into the ledger as chapters on the artefact's clock, written out as a file the ledger was told about first, and handed to the programme as a second input")]
    public async Task ADetectionThatMarkedSomethingIsWrittenIntoTheLedgerAndHandedToTheProgramme()
    {
        using var harness = new EncodeHarness();
        string arguments = harness.Room.Under("arguments");
        harness.Standing($"printf '%s\\n' \"$@\" > \"{arguments}\"; printf 'the picture' > \"$destination\"");
        harness.Heads.Reading = SourceHeadReading.Read(MeasuredHeads.Start, MeasuredHeads.Start + Skipped);
        EncodeJob job = harness.Running(APodInTheMiddle(harness).Id, harness.Defined().Id);
        string? seenWhenRecorded = null;
        harness.Scratch.WhenRecording = scratch =>
        {
            if (scratch.Kind is EncodeScratchKind.Chapters)
            {
                seenWhenRecorded = File.Exists(harness.ChaptersPathOf(job)) ? "it was already written" : "nothing was there yet";
            }
        };

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal("nothing was there yet", seenWhenRecorded);
        Assert.Equal(
            [job.ChaptersFileName.Value, job.WorkFileName.Value],
            harness.Scratch.Files.Select(scratch => scratch.FileName.Value));
        Assert.Equal(
            [EncodeScratchKind.Chapters, EncodeScratchKind.WorkFile],
            harness.Scratch.Files.Select(scratch => scratch.Kind));

        Assert.Equal(
            [
                (EncodeChapter.FirstOrdinal, TimeSpan.Zero, TimeSpan.FromSeconds(2), ChapterKind.Programme),
                (1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), ChapterKind.Break),
                (2, TimeSpan.FromSeconds(5), Artefact, ChapterKind.Programme),
            ],
            harness.Chapters.Chapters.Select(chapter => (chapter.Ordinal, chapter.StartsAt, chapter.EndsAt, chapter.Kind)));
        Assert.All(harness.Chapters.Chapters, chapter => Assert.Equal(job.Id, chapter.JobId));

        string[] handed = File.ReadAllLines(arguments);
        int breaks = Array.IndexOf(handed, harness.ChaptersPathOf(job));
        Assert.Contains("ffmetadata", handed);
        Assert.Contains("-map_chapters", handed);
        Assert.True(breaks > Array.IndexOf(handed, harness.SourcePathOf(harness.Recordings.Rows[0])), "the chapters are the input after the recording");
    }

    [Fact(DisplayName = "the reading is in the ledger before the programme that bakes it in is started, and what was marked is in the ledger before the file it is written to exists")]
    public async Task TheReadingIsInTheLedgerBeforeTheProgrammeStarts()
    {
        using var harness = new EncodeHarness();
        string marker = harness.Room.Under("it-ran");
        harness.Standing($"printf ran > \"{marker}\"; printf 'the picture' > \"$destination\"");
        harness.Heads.Reading = SourceHeadReading.Read(MeasuredHeads.Start, MeasuredHeads.Start + Skipped);
        EncodeJob job = harness.Running(APodInTheMiddle(harness).Id, harness.Defined().Id);
        List<ChapterVerdict?> saved = [];
        harness.Jobs.WhenSaving = saving => saved.Add(saving.Chapters?.Verdict);
        bool nothingHadRunWhenRecorded = false;
        harness.Chapters.WhenRecording = _ => nothingHadRunWhenRecorded = !File.Exists(marker);

        await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(ChapterVerdict.Marked, saved[0]);
        Assert.True(nothingHadRunWhenRecorded, "the chapters were in the ledger while the programme had not yet run");
        Assert.True(File.Exists(marker), "the programme ran");
    }

    [Fact(DisplayName = "the chapters file is swept by the ledger when the job ends, the same as any other scratch")]
    public async Task TheChaptersFileIsSweptByTheLedgerWhenTheJobEnds()
    {
        using var harness = new EncodeHarness();
        harness.Standing("printf 'the picture' > \"$destination\"");
        harness.Heads.Reading = SourceHeadReading.Read(MeasuredHeads.Start, MeasuredHeads.Start + Skipped);
        EncodeJob job = harness.Running(APodInTheMiddle(harness).Id, harness.Defined().Id);

        await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, job.Status);
        Assert.False(File.Exists(harness.ChaptersPathOf(job)));
        Assert.Equal(
            [EncodeScratchFate.Removed, EncodeScratchFate.BecameTheArtefact],
            harness.Scratch.Files.Select(scratch => scratch.Fate));
    }

    [Fact(DisplayName = "an artefact larger than its recording is made again four coarser, the smaller second is placed, and the job says it made it again because the first was larger")]
    public async Task AnArtefactLargerThanItsRecordingIsMadeAgainCoarser()
    {
        using var harness = new EncodeHarness();
        string runs = harness.Room.Under("runs");
        harness.Standing(Counted(Writes(Larger, Small), runs));
        List<EncodeRemake?> saved = [];
        harness.Jobs.WhenSaving = job => saved.Add(job.Remade);
        Recording recording = harness.Recorded();
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(Small, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Equal(2, File.ReadAllLines(runs).Length);
        Assert.Equal(EncodeRemake.LargerThanTheSource, job.Remade);
        Assert.Contains(EncodeRemake.LargerThanTheSource, saved);
        Assert.Equal(EncodeHarness.Broadcast, File.ReadAllText(harness.SourcePathOf(recording)));
        Assert.False(File.Exists(harness.WorkPathOf(job)));
        Assert.False(File.Exists(RemadePathOf(harness, job)));
        Assert.Equal(
            [$"recorded {job.WorkFileName.Value}", $"recorded {job.RemadeFileName.Value}"],
            harness.Scratch.Moves.Where(move => move.StartsWith("recorded", StringComparison.Ordinal)));
        Assert.Equal(EncodeScratchFate.Removed, harness.Scratch.Files.Single(file => file.FileName.Equals(job.WorkFileName)).Fate);
        Assert.Equal(EncodeScratchFate.BecameTheArtefact, harness.Scratch.Files.Single(file => file.FileName.Equals(job.RemadeFileName)).Fate);
        RemovedBeforeThePlacing(harness, unused: job.WorkFileName, placed: job.RemadeFileName);
        Assert.Contains(harness.RunnerLog.Warnings, line => line.Contains("larger than its recording", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "a second try still larger than the recording but smaller than the first is placed, and the job says it kept something still larger")]
    public async Task ASecondTryStillLargerButSmallerThanTheFirstIsPlaced()
    {
        using var harness = new EncodeHarness();
        harness.Standing(Writes(Larger, StillLarger));
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(StillLarger, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Equal(EncodeRemake.StillLargerThanTheSource, job.Remade);
    }

    [Fact(DisplayName = "a second try no smaller than the first leaves the first to be placed and is removed, and the job says the second came out no smaller")]
    public async Task ASecondTryNoSmallerThanTheFirstLeavesTheFirst()
    {
        using var harness = new EncodeHarness();
        harness.Standing(Writes(StillLarger, Larger));
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(StillLarger, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Equal(EncodeRemake.NoSmallerTheSecondTime, job.Remade);
        Assert.Equal(EncodeScratchFate.BecameTheArtefact, harness.Scratch.Files.Single(file => file.FileName.Equals(job.WorkFileName)).Fate);
        Assert.False(File.Exists(RemadePathOf(harness, job)));
        Assert.Equal(EncodeScratchFate.Removed, harness.Scratch.Files.Single(file => file.FileName.Equals(job.RemadeFileName)).Fate);
        RemovedBeforeThePlacing(harness, unused: job.RemadeFileName, placed: job.WorkFileName);
    }

    [Fact(DisplayName = "a second try that fails leaves the first to be placed, the job completes, and it says the second came out no smaller")]
    public async Task ASecondTryThatFailsLeavesTheFirst()
    {
        using var harness = new EncodeHarness();
        harness.Standing($"""
            case " $* " in
              *" -crf 26 "*) printf 'half a picture' > "$destination"; exit 187 ;;
              *) printf '{Larger}' > "$destination" ;;
            esac
            """);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Null(job.Failure);
        Assert.Equal(Larger, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Equal(EncodeRemake.NoSmallerTheSecondTime, job.Remade);
        Assert.False(File.Exists(RemadePathOf(harness, job)));
        RemovedBeforeThePlacing(harness, unused: job.RemadeFileName, placed: job.WorkFileName);
        Assert.Contains(
            harness.RunnerLog.Warnings,
            line => line.Contains("187", StringComparison.Ordinal) && line.Contains(nameof(EncodeFailure.FfmpegExitedNonZero), StringComparison.Ordinal));
    }

    [Fact(DisplayName = "a second try that runs out of room is told apart in the log from one the programme refused, the job completes on the first, and what the second left is removed at once")]
    public async Task ASecondTryThatRunsOutOfRoomIsToldApart()
    {
        using var harness = new EncodeHarness();
        harness.Standing($"""
            case " $* " in
              *" -crf 26 "*) printf 'half a picture' > "$destination"; echo "av_interleaved_write_frame(): No space left on device" >&2; exit 1 ;;
              *) printf '{Larger}' > "$destination" ;;
            esac
            """);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(Larger, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Equal(EncodeRemake.NoSmallerTheSecondTime, job.Remade);
        Assert.False(File.Exists(RemadePathOf(harness, job)));
        RemovedBeforeThePlacing(harness, unused: job.RemadeFileName, placed: job.WorkFileName);
        Assert.Contains(harness.RunnerLog.Warnings, line => line.Contains(nameof(EncodeFailure.NotEnoughRoom), StringComparison.Ordinal));
        Assert.DoesNotContain(harness.RunnerLog.Warnings, line => line.Contains(nameof(EncodeFailure.FfmpegExitedNonZero), StringComparison.Ordinal));
    }

    [Fact(DisplayName = "a recording whose captions were taken has the text track put into the second it kept, the first is gone before the captions are made, and what is placed is the copy with the track")]
    public async Task TheTextTrackGoesIntoTheSecondItKept()
    {
        using var harness = new EncodeHarness();
        string muxed = harness.Room.Under("muxed");
        string withTheTrack = harness.Room.Under("with-the-track.mp4");
        File.WriteAllBytes(withTheTrack, Mp4WithASubtitleTrack());
        harness.Standing($"""
            case "$destination" in
              *{EncodeFileName.CaptionedExtension})
                taken=""
                previous=""
                for argument in "$@"; do
                  if [ "$previous" = "-i" ] && [ -z "$taken" ]; then taken=$argument; fi
                  previous=$argument
                done
                printf '%s' "$taken" > "{muxed}"
                cp "{withTheTrack}" "$destination" ;;
              *)
                {Writes(Larger, Small)} ;;
            esac
            """);
        harness.Programmes = harness.Programmes with { Prober = Prober(harness) };
        Recording recording = harness.Recorded();
        CaptionRecord record = new(
            1440,
            1080,
            MeasuredHeads.Start,
            [new CaptionCue(TwoSecondsIn, null)],
            [new CaptionLine(TwoSecondsIn, "合成字幕")]);
        await harness.CaptionShelf.KeepAsync(recording.Id, record, Cancel);
        recording.Caption(CaptionState.Ready, 1, EncodeHarness.Queued.AddMinutes(20));
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(EncodeRemake.LargerThanTheSource, job.Remade);
        Assert.Equal(EncodeCaptionTrack.Added, job.CaptionTrack);
        Assert.Equal(RemadePathOf(harness, job), File.ReadAllText(muxed));
        Assert.Equal(job.CaptionedFileName, job.FileToPlace);
        Assert.Equal(File.ReadAllBytes(withTheTrack).Length, File.ReadAllBytes(harness.ArtefactPathOf(job)).Length);
        Assert.Equal(EncodeScratchFate.BecameTheArtefact, harness.Scratch.Files.Single(file => file.FileName.Equals(job.CaptionedFileName)).Fate);

        List<string> moves = harness.Scratch.Moves;
        int firstRemoved = moves.IndexOf($"settled {job.WorkFileName.Value} Removed");
        int captionsRecorded = moves.IndexOf($"recorded {job.CaptionTrackFileName.Value}");

        Assert.True(firstRemoved >= 0 && captionsRecorded > firstRemoved, string.Join('\n', moves));
        Assert.DoesNotContain(Directory.EnumerateFiles(harness.WorkDirectory), file => !file.EndsWith(EncodeFileName.ArtefactExtension, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "a stop asked for during the second try leaves the job running with what it was making again written down and both work files where they are, and starting over forgets it and writes new ones")]
    public async Task AStopDuringTheSecondTryLeavesTheJobRunningAndStartingOverForgetsIt()
    {
        using var harness = new EncodeHarness();
        string began = harness.Room.Under("began");
        harness.Standing($"""
            case " $* " in
              *" -crf 26 "*)
                if [ ! -e "{began}" ]; then
                  printf 'half a picture' > "$destination"
                  printf '%s' "$$" > "{began}"
                  exec sleep 30
                fi
                printf '{Small}' > "$destination" ;;
              *) printf '{Larger}' > "$destination" ;;
            esac
            """);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);
        EncodeFileName firstWork = job.WorkFileName;
        EncodeFileName firstRemade = job.RemadeFileName;
        using var stopping = new CancellationTokenSource();

        Task<EncodeJobStatus> running = harness.Runner.RunAsync(job, stopping.Token);
        await Eventually.Happens(() => File.Exists(began), "the second try began");
        await stopping.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(EncodeJobStatus.Running, job.Status);
        Assert.Equal(EncodeRemake.LargerThanTheSource, job.Remade);
        Assert.Equal(2, harness.Scratch.Files.Count);
        Assert.All(harness.Scratch.Files, file => Assert.True(file.IsOwedARemoval, $"{file.FileName.Value} is left for the next start"));
        Assert.True(File.Exists(Path.Combine(harness.WorkDirectory, firstWork.Value)));
        Assert.True(File.Exists(Path.Combine(harness.WorkDirectory, firstRemade.Value)));

        job.Requeue(EncodeHarness.Started.AddMinutes(5));

        Assert.Null(job.Remade);
        Assert.NotEqual(firstWork, job.WorkFileName);
        Assert.NotEqual(firstRemade, job.RemadeFileName);

        job.Start(EncodeHarness.Started.AddMinutes(6));
        EncodeJobStatus again = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, again);
        Assert.Equal(Small, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Equal(EncodeRemake.LargerThanTheSource, job.Remade);
        Assert.Contains($"recorded {job.WorkFileName.Value}", harness.Scratch.Moves);
        Assert.Contains($"recorded {job.RemadeFileName.Value}", harness.Scratch.Moves);
        Assert.DoesNotContain(Directory.EnumerateFiles(harness.WorkDirectory), file => file.EndsWith(EncodeFileName.WorkExtension, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "a job called off while it makes its artefact again has both its work files swept, and its run is dropped for the ledger's word")]
    public async Task AJobCalledOffWhileItMakesItAgainHasBothWorkFilesSwept()
    {
        using var harness = new EncodeHarness();
        harness.Jobs.KeepsItsOwnRows = true;
        string began = harness.Room.Under("began");
        harness.Standing($"""
            case " $* " in
              *" -crf 26 "*)
                printf 'half a picture' > "$destination"
                printf '%s' "$$" > "{began}"
                exec sleep 30 ;;
              *) printf '{Larger}' > "$destination" ;;
            esac
            """);
        Recording recording = harness.Recorded();
        harness.Jobs.Jobs.Add(EncodeJob.Queue(
            EncodeJobId.New(),
            recording.Id,
            harness.Defined().Id,
            EncodeDestinationId.New(),
            EncodeHarness.Encodes,
            EncodeHarness.Queued));
        EncodeJob job = (await harness.Jobs.ClaimNextAsync(EncodeHarness.Started, Cancel)).Job!;
        EncodeJob? calledOff = null;
        harness.Jobs.WhenSaving = saved =>
        {
            if (calledOff is not null && !ReferenceEquals(saved, calledOff))
            {
                throw new EncodeJobMovedMeanwhileException(saved.Id);
            }
        };

        Task<EncodeJobStatus> running = harness.Runner.RunAsync(job, Cancel);
        await Eventually.Happens(
            () => File.Exists(began)
                && harness.Jobs.Jobs.Single().Programme?.ProcessId == int.Parse(File.ReadAllText(began), CultureInfo.InvariantCulture),
            "the second try began and the ledger holds its programme");

        EncodeJob ledger = (await harness.Jobs.FindAsync(job.Id, Cancel))!;
        RunningProgramme second = ledger.Programme!;
        calledOff = ledger;
        ledger.Cancel(harness.Clock.GetUtcNow().UtcDateTime);
        await harness.Jobs.SaveAsync(ledger, Cancel);
        StrayFate stopped = new StrayProgrammes().Stop(second);
        await harness.Cleaner.ClearAsync(ledger, Cancel);

        await Assert.ThrowsAsync<EncodeJobMovedMeanwhileException>(() => running);
        Assert.Equal(StrayFate.Stopped, stopped);
        Assert.Equal(EncodeJobStatus.Cancelled, harness.Jobs.Jobs.Single().Status);
        Assert.False(File.Exists(Path.Combine(harness.WorkDirectory, job.WorkFileName.Value)));
        Assert.False(File.Exists(RemadePathOf(harness, job)));
        Assert.Equal(2, harness.Scratch.Files.Count);
        Assert.All(harness.Scratch.Files, file => Assert.Equal(EncodeScratchFate.Removed, file.Fate));
    }

    [Fact(DisplayName = "on the card the second try is handed the quantiser four coarser")]
    public async Task OnTheCardTheSecondTryIsHandedTheQuantiserFourCoarser()
    {
        using var harness = new EncodeHarness();
        harness.Settings = harness.Settings with { Prefer = EncodeEncoder.Vaapi };
        harness.Machine.Can = MachineCapabilities.Of(
            CardStanding.Usable,
            [Faculty.EncodeH264OnTheProcessor, Faculty.EncodeH264OnTheCard],
            string.Empty);
        harness.Standing(Writes(Larger, Small, coarser: "-qp 28"));
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(Small, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Equal(EncodeRemake.LargerThanTheSource, job.Remade);
    }

    [Fact(DisplayName = "an artefact no larger than its recording is made once")]
    public async Task AnArtefactNoLargerThanItsRecordingIsMadeOnce()
    {
        using var harness = new EncodeHarness();
        string runs = harness.Room.Under("runs");
        harness.Standing(Counted(Writes(EncodeHarness.Broadcast, Small), runs));
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(EncodeHarness.Broadcast, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Single(File.ReadAllLines(runs));
        Assert.Null(job.Remade);
        Assert.DoesNotContain(harness.Scratch.Files, file => file.FileName.Equals(job.RemadeFileName));
    }

    [Fact(DisplayName = "a recording whose file is gone by the time the encode ends is not measured against, and the artefact is placed as it was made")]
    public async Task ARecordingWhoseFileIsGoneIsNotMeasuredAgainst()
    {
        using var harness = new EncodeHarness();
        string runs = harness.Room.Under("runs");
        Recording recording = harness.Recorded();
        harness.Standing(Counted($"rm -f \"{harness.SourcePathOf(recording)}\"\n{Writes(Larger, Small)}", runs));
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(Larger, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Single(File.ReadAllLines(runs));
        Assert.Null(job.Remade);
    }

    [Fact(DisplayName = "a profile already at the coarsest is not made again, since a second try could come out no smaller")]
    public async Task AProfileAlreadyAtTheCoarsestIsNotMadeAgain()
    {
        using var harness = new EncodeHarness();
        string runs = harness.Room.Under("runs");
        harness.Standing(Counted(Writes(Larger, Small), runs));
        EncodeProfile coarsest = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Coarsest"),
            EncodeCodec.H264,
            EncodeResolution.AsSource,
            Deinterlace.EveryField,
            new ConstantRateFactor(ConstantRateFactor.Coarsest),
            new ConstantQuantiser(ConstantQuantiser.Coarsest),
            EncodeHarness.Queued);
        harness.Profiles.Profiles.Add(coarsest);
        EncodeJob job = harness.Running(harness.Recorded().Id, coarsest.Id);

        EncodeJobStatus ended = await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(EncodeJobStatus.Completed, ended);
        Assert.Equal(Larger, File.ReadAllText(harness.ArtefactPathOf(job)));
        Assert.Single(File.ReadAllLines(runs));
        Assert.Null(job.Remade);
    }

    [Fact(DisplayName = "BR-CD-010: a detection that marked breaks is embedded in the recording's own time, titled for the programme and the break, and the ledger keeps it came from the segments and which version, with the chapters on the artefact's clock")]
    public async Task ADetectionThatMarkedBreaksIsEmbeddedInTheRecordingsOwnTime()
    {
        using var harness = new EncodeHarness();
        Run run = Watched(harness);
        harness.Versions.Active = HeldSegmentModelVersions.InUse(2, HeldSegmentModelVersions.Installed);
        Recording recording = harness.Recorded();
        harness.Heads.Reading = SourceHeadReading.Read(MeasuredHeads.Start, MeasuredHeads.Start + Skipped);
        RecordingDetection detection = harness.Detected(
            recording,
            SegmentDetectionState.Marked,
            Span(0, 3, SegmentKind.Programme),
            Span(3, 6.25, SegmentKind.Break),
            Span(6.25, 10, SegmentKind.Programme));
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        Assert.Equal(EncodeJobStatus.Completed, await harness.Runner.RunAsync(job, Cancel));

        Assert.Equal(
            """
            ;FFMETADATA1
            [CHAPTER]
            TIMEBASE=1/1000
            START=0
            END=3000
            title=本編
            [CHAPTER]
            TIMEBASE=1/1000
            START=3000
            END=6250
            title=CM
            [CHAPTER]
            TIMEBASE=1/1000
            START=6250
            END=10000
            title=本編

            """.ReplaceLineEndings("\n"),
            File.ReadAllText(run.Chapters));
        Assert.Equal(
            (ChapterDetectorName.Segments, ChapterVerdict.Marked, 2),
            (job.Chapters!.Detector, job.Chapters.Verdict, job.Chapters.ModelVersion));
        Assert.Equal(detection.CmShare, job.Chapters.BreakShare);
        Assert.Equal(
            [
                (TimeSpan.Zero, TimeSpan.FromSeconds(2.5), ChapterKind.Programme),
                (TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5.75), ChapterKind.Break),
                (TimeSpan.FromSeconds(5.75), Artefact, ChapterKind.Programme),
            ],
            harness.Chapters.Chapters.Select(chapter => (chapter.StartsAt, chapter.EndsAt, chapter.Kind)));

        string[] handed = run.Handed(harness, job);
        Assert.Equal(["-f", "ffmetadata", "-i", Metadata], handed.SkipWhile(argument => argument != Source).Skip(1).Take(4));
        Assert.Contains("-map_chapters", handed);
    }

    [Fact(DisplayName = "BR-CD-010: an opening and an ending the detection found are embedded titled OP and ED, and the ledger keeps them by those kinds on the artefact's clock")]
    public async Task AnOpeningAndAnEndingAreEmbeddedTitledOpAndEd()
    {
        using var harness = new EncodeHarness();
        Run run = Watched(harness);
        Recording recording = harness.Recorded();
        harness.Heads.Reading = SourceHeadReading.Read(MeasuredHeads.Start, MeasuredHeads.Start + Skipped);
        harness.Detected(
            recording,
            SegmentDetectionState.Marked,
            Span(0, 2, SegmentKind.Opening),
            Span(2, 5, SegmentKind.Programme),
            Span(5, 6, SegmentKind.Break),
            Span(6, 8.5, SegmentKind.Programme),
            Span(8.5, 10, SegmentKind.Ending));
        EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

        Assert.Equal(EncodeJobStatus.Completed, await harness.Runner.RunAsync(job, Cancel));

        Assert.Equal(
            ["title=OP", "title=本編", "title=CM", "title=本編", "title=ED"],
            File.ReadAllLines(run.Chapters).Where(line => line.StartsWith("title=", StringComparison.Ordinal)));
        Assert.Equal(
            [
                (TimeSpan.Zero, TimeSpan.FromSeconds(1.5), ChapterKind.Opening),
                (TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(4.5), ChapterKind.Programme),
                (TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(5.5), ChapterKind.Break),
                (TimeSpan.FromSeconds(5.5), TimeSpan.FromSeconds(8), ChapterKind.Programme),
                (TimeSpan.FromSeconds(8), Artefact, ChapterKind.Ending),
            ],
            harness.Chapters.Chapters.Select(chapter => (chapter.StartsAt, chapter.EndsAt, chapter.Kind)));
    }

    [Fact(DisplayName = "BR-CD-010: a detection that found only the programme, or was thrown away, is encoded with the arguments of a run with no detection, and the ledger keeps which version said so")]
    public async Task ADetectionWithNothingToEmbedIsEncodedWithNoChapters()
    {
        string[] none = await UndetectedAsync();
        (SegmentDetectionState State, SegmentSpan[] Stretches)[] nothingToEmbed =
        [
            (SegmentDetectionState.NothingFound, [Span(0, 10, SegmentKind.Programme)]),
            (SegmentDetectionState.Discarded, [Span(0, 4, SegmentKind.Programme), Span(4, 10, SegmentKind.Break)]),
        ];

        foreach ((SegmentDetectionState state, SegmentSpan[] stretches) in nothingToEmbed)
        {
            using var harness = new EncodeHarness();
            Run run = Watched(harness);
            Recording recording = harness.Recorded();
            harness.Detected(recording, state, stretches);
            EncodeJob job = harness.Running(recording.Id, harness.Defined().Id);

            Assert.Equal(EncodeJobStatus.Completed, await harness.Runner.RunAsync(job, Cancel));

            Assert.Equal(none, run.Handed(harness, job));
            Assert.Equal(
                (ChapterDetectorName.Segments, state is SegmentDetectionState.Discarded ? ChapterVerdict.Discarded : ChapterVerdict.NothingFound, 1),
                (job.Chapters!.Detector, job.Chapters.Verdict, job.Chapters.ModelVersion));
            Assert.Empty(harness.Chapters.Chapters);
            Assert.Equal([EncodeScratchKind.WorkFile], harness.Scratch.Files.Select(scratch => scratch.Kind));
        }
    }

    [Fact(DisplayName = "BR-CD-010: with no detection the encode goes ahead at once with no chapters, and the ledger keeps that the segments had nothing to give yet")]
    public async Task WithNoDetectionTheEncodeHasNoChapters()
    {
        using var harness = new EncodeHarness();
        Run run = Watched(harness);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        Assert.Equal(EncodeJobStatus.Completed, await harness.Runner.RunAsync(job, Cancel));

        string[] handed = run.Handed(harness, job);
        Assert.DoesNotContain("ffmetadata", handed);
        Assert.DoesNotContain("-map_chapters", handed);
        Assert.Equal(
            (ChapterDetectorName.Segments, ChapterVerdict.NotAsked, (int?)null),
            (job.Chapters!.Detector, job.Chapters.Verdict, job.Chapters.ModelVersion));
        Assert.Empty(harness.Chapters.Chapters);
        Assert.False(File.Exists(harness.ChaptersPathOf(job)));
        Assert.Equal([EncodeScratchKind.WorkFile], harness.Scratch.Files.Select(scratch => scratch.Kind));
    }

    /// <summary>
    /// A recording the version in use detected with a pod of adverts in the middle, which falls two to five seconds
    /// into the artefact once the head is skipped.
    /// </summary>
    private static Recording APodInTheMiddle(EncodeHarness harness)
    {
        Recording recording = harness.Recorded();
        harness.Detected(
            recording,
            SegmentDetectionState.Marked,
            new SegmentSpan(TimeSpan.Zero, TimeSpan.FromSeconds(2.5), SegmentKind.Programme, 0.9),
            new SegmentSpan(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5.5), SegmentKind.Break, 0.9),
            new SegmentSpan(TimeSpan.FromSeconds(5.5), EncodeHarness.Whole, SegmentKind.Programme, 0.9));

        return recording;
    }

    private static string Writes(string first, string second, string coarser = "-crf 26")
        => $"""
            case " $* " in
              *" {coarser} "*) printf '{second}' > "$destination" ;;
              *) printf '{first}' > "$destination" ;;
            esac
            printf 'out_time_us=10000000\nspeed=2.0x\nprogress=end\n'
            """;

    private static string Counted(string script, string runs)
        => $"printf 'run\\n' >> \"{runs}\"\n{script}";

    private static string RemadePathOf(EncodeHarness harness, EncodeJob job)
        => Path.Combine(harness.WorkDirectory, job.RemadeFileName.Value);

    private static void RemovedBeforeThePlacing(EncodeHarness harness, EncodeFileName unused, EncodeFileName placed)
    {
        List<string> moves = harness.Scratch.Moves;
        int removed = moves.IndexOf($"settled {unused.Value} Removed");
        int became = moves.IndexOf($"settled {placed.Value} BecameTheArtefact");

        Assert.True(removed >= 0 && became > removed, string.Join('\n', moves));
    }

    /// <summary>
    /// A stand-in for ffprobe that says the file with the text track put in carries one Japanese text track off
    /// by default beside the picture and the sound, and that anything else carries the picture and the sound.
    /// </summary>
    private static string Prober(EncodeHarness harness)
        => StandInProgramme.Written(
            harness.Room.Under($"prober-{Guid.NewGuid():N}.sh"),
            $"""
            for argument in "$@"; do file=$argument; done
            case "$file" in
              *{EncodeFileName.CaptionedExtension}) printf '%s' '{CarriedWithTheTrack}' ;;
              *) printf '%s' '{CarriedWithout}' ;;
            esac
            """);

    private static byte[] Mp4WithASubtitleTrack()
        => [
            .. Box("ftyp", Encoding.ASCII.GetBytes("isom\0\0\x02\0")),
            .. Box("moov", [.. Track("vide"), .. Track("soun"), .. Track("sbtl")]),
            .. Box("mdat", new byte[64]),
        ];

    private static byte[] Track(string handler)
    {
        byte[] header = new byte[84];
        header[3] = 0x03;

        byte[] reference = new byte[24];
        Encoding.ASCII.GetBytes(handler).CopyTo(reference, 8);

        return Box("trak", [.. Box("tkhd", header), .. Box("mdia", [.. Box("mdhd", new byte[24]), .. Box("hdlr", reference)])]);
    }

    private static byte[] Box(string type, byte[] body)
    {
        byte[] box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        body.CopyTo(box, 8);

        return box;
    }

    /// <summary>
    /// The arguments of a run of a recording no version has detected.
    /// </summary>
    private static async Task<string[]> UndetectedAsync()
    {
        using var harness = new EncodeHarness();
        Run run = Watched(harness);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        Assert.Equal(EncodeJobStatus.Completed, await harness.Runner.RunAsync(job, Cancel));
        Assert.Equal(ChapterVerdict.NotAsked, job.Chapters!.Verdict);

        return run.Handed(harness, job);
    }

    private static Run Watched(EncodeHarness harness)
    {
        string arguments = harness.Room.Under("arguments");
        string chapters = harness.Room.Under("chapters-handed");
        harness.Standing(
            $"printf '%s\\n' \"$@\" > \"{arguments}\"; "
            + $"for given in \"$@\"; do case \"$given\" in *{EncodeFileName.ChaptersExtension}) cp \"$given\" \"{chapters}\";; esac; done; "
            + "printf 'the picture' > \"$destination\"");

        return new Run(arguments, chapters);
    }

    private static SegmentSpan Span(double starts, double ends, SegmentKind kind)
        => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends), kind, 0.9);

    /// <summary>
    /// What the programme standing in for ffmpeg was handed, and where it kept the chapters it was handed.
    /// </summary>
    private sealed record Run(string Arguments, string Chapters)
    {
        public string[] Handed(EncodeHarness harness, EncodeJob job)
            => [.. File.ReadAllLines(Arguments).Select(argument => Named(harness, job, argument))];

        private static string Named(EncodeHarness harness, EncodeJob job, string argument)
        {
            if (argument == harness.SourcePathOf(harness.Recordings.Rows[0]))
            {
                return Source;
            }

            if (argument == harness.WorkPathOf(job))
            {
                return Work;
            }

            return argument == harness.ChaptersPathOf(job) ? Metadata : argument;
        }
    }
}
