using System.Diagnostics;

using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class EncodeJobRunnerTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const string WritesTheWorkFileAndReportsProgress = """
        printf 'out_time_us=0\nspeed=1.0x\nprogress=continue\n'
        printf 'out_time_us=5000000\nspeed=2.0x\nprogress=continue\n'
        printf 'the picture' > "$destination"
        printf 'out_time_us=10000000\nspeed=2.0x\nprogress=end\n'
        """;

    [Fact(DisplayName = "BR-ES-001: a job is run to its end: the programme writes the work file the ledger was told about, the artefact is placed by the ledger, and the job completes")]
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
        Assert.Equal([harness.SourcePathOf(recording)], harness.Lengths.Asked);
    }

    [Fact(DisplayName = "BR-ED2-013: how far the job has got is told from 0 to 100 as the programme reports it")]
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

    [Fact(DisplayName = "BR-ED2-010: the work file is written into the ledger before the programme is started, and only then does the programme get its path")]
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

    [Fact(DisplayName = "BR-ED2-012: a recording that is not where the ledger says fails as source missing, and the programme is never started")]
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

    [Fact(DisplayName = "BR-ED2-012: a recording file that holds nothing is source missing too, and a recording the ledger does not hold at all")]
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

    [Fact(DisplayName = "BR-ED2-012: a programme that exits non-zero fails the job as such, with the tail of what it said beside the classification and no path in it, and the work file is swept by the ledger")]
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

    [Fact(DisplayName = "BR-ED2-012: a programme that ran out of room is told apart from one that refused")]
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

    [Fact(DisplayName = "BR-ED2-014: a programme that stops reporting progress is stopped where it stands and the job fails as timed out")]
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

    [Fact(DisplayName = "BR-EV-004: a codec this machine cannot encode anywhere fails the job as capability unavailable, and the programme is never started")]
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

    [Fact(DisplayName = "BR-EV-004: a card asked for and out of reach degrades to the processor, the run is written down as degraded, and the job still completes")]
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

    [Fact(DisplayName = "BR-EV-002: the programme is handed the recording as one argument and the work file as the last, and nothing a broadcaster wrote")]
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

    [Fact(DisplayName = "BR-ED2-011: a stop asked for while the programme runs stops the programme and leaves the job running in the ledger, programme and all, for the next start to put back")]
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

    [Fact(DisplayName = "BR-ED2-009: a job whose source is under a root this process cannot place is refused without touching anything")]
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

    [Fact(DisplayName = "BR-ES-001: only a job the ledger holds as running is run")]
    public async Task OnlyARunningJobIsRun()
    {
        using var harness = new EncodeHarness();
        EncodeJob waiting = EncodeJob.Queue(EncodeJobId.New(), RecordingId.New(), EncodeProfileId.New(), EncodeDestinationId.New(), EncodeHarness.Primary, EncodeHarness.Queued);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Runner.RunAsync(waiting, Cancel));
    }

    [Fact(DisplayName = "BR-ED2-011: the programme's id and start are written into the ledger before it has reported anything, and let go of once the job has ended")]
    public async Task TheProgrammesIdAndStartAreWrittenIntoTheLedgerBeforeItReportsAnything()
    {
        using var harness = new EncodeHarness();
        harness.Standing(WritesTheWorkFileAndReportsProgress);
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);
        List<(RunningProgramme? Programme, EncodeHeadway? Headway, EncodeJobStatus Status)> saved = [];
        harness.Jobs.WhenSaving = saving => saved.Add((saving.Programme, saving.Headway, saving.Status));

        await harness.Runner.RunAsync(job, Cancel);

        (RunningProgramme? programme, EncodeHeadway? headway, EncodeJobStatus status) = saved[0];
        Assert.Equal(EncodeJobStatus.Running, status);
        Assert.NotNull(programme);
        Assert.Null(headway);
        Assert.InRange(programme.ProcessId, 2, int.MaxValue);
        Assert.InRange(programme.StartedAt, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddSeconds(2));
        Assert.Null(job.Programme);
        Assert.Equal(EncodeJobStatus.Completed, job.Status);
    }

    [Fact(DisplayName = "BR-EV-004: where the run went is written on the job — asked for the card, ran on the processor — and stays written once it has ended")]
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

    [Fact(DisplayName = "BR-EV-004: a run that went where it was sent is written down as such, with no swerve")]
    public async Task ARunThatWentWhereItWasSentIsWrittenDownAsSuch()
    {
        using var harness = new EncodeHarness();
        harness.Standing("printf 'the picture' > \"$destination\"");
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        await harness.Runner.RunAsync(job, Cancel);

        Assert.Equal(new EncodeRoute(EncodeEncoder.Software, EncodeEncoder.Software, null), job.Route);
    }

    [Fact(DisplayName = "BR-ED2-014: headway is written into the ledger as the programme reports it — the portion, what is left and when — and the last of it stays with the job that ended")]
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

    [Fact(DisplayName = "BR-ED2-014: a programme that reports often is written into the ledger at every tenth and at least every heartbeat, not at every report")]
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

    [Fact(DisplayName = "BR-ED2-005: the programme is handed the core cap, and no more cores than this machine has")]
    public async Task TheProgrammeIsHandedTheCoreCap()
    {
        using var harness = new EncodeHarness();
        harness.Settings = harness.Settings with { MostCores = 1 };
        string arguments = harness.Room.Under("arguments");
        harness.Standing($"printf '%s\\n' \"$@\" > \"{arguments}\"; printf 'the picture' > \"$destination\"");
        EncodeJob job = harness.Running(harness.Recorded().Id, harness.Defined().Id);

        await harness.Runner.RunAsync(job, Cancel);

        string[] handed = File.ReadAllLines(arguments);
        Assert.Equal(2, handed.Count(argument => argument == "-threads"));
        Assert.Equal("1", handed[Array.IndexOf(handed, "-threads") + 1]);
        Assert.Equal("1", handed[Array.IndexOf(handed, "-filter_threads") + 1]);

        harness.Settings = harness.Settings with { MostCores = Environment.ProcessorCount + 40 };
        await harness.Runner.RunAsync(harness.Running(harness.Recorded().Id, harness.Defined().Id), Cancel);
        handed = File.ReadAllLines(arguments);
        Assert.Equal(Environment.ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture), handed[Array.IndexOf(handed, "-threads") + 1]);
    }

    [Fact(DisplayName = "BR-EV-004: a programme that is not on this machine fails the job as capability unavailable rather than blaming the recording")]
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
}
