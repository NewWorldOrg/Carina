using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class EncodeRestartTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Now = new(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

    private static readonly RunningProgramme Ffmpeg = new(31337, EncodeHarness.Started.AddSeconds(1));

    private static readonly RunningProgramme Another = new(31338, EncodeHarness.Started.AddSeconds(2));

    [Fact(DisplayName = "the programme a running job wrote down is stopped before the job is put back, and the job carries it no longer")]
    public async Task TheProgrammeARunningJobWroteDownIsStoppedBeforeTheJobIsPutBack()
    {
        var held = new HeldEncodeJobs();
        var strays = new ScriptedStrays();
        EncodeJob job = Running(Ffmpeg);
        held.Jobs.Add(job);
        bool stoppedBeforeSaved = false;
        held.WhenSaving = _ => stoppedBeforeSaved = strays.Asked.Count is 1;

        EncodeRestartReport report = await Restart(held, strays).RecoverAsync(Cancel);

        Assert.Equal([Ffmpeg], strays.Asked);
        Assert.True(stoppedBeforeSaved, "the programme was stopped before the ledger was written");
        Assert.Equal(1, report.Stopped);
        Assert.Equal(0, report.Spared);
        Assert.Equal(1, report.PutBack);
        Assert.Equal(EncodeJobStatus.Queued, job.Status);
        Assert.Null(job.Programme);
    }

    [Fact(DisplayName = "a programme under the written id that began at another time is spared, and the job is put back all the same")]
    public async Task AProgrammeThatBeganAtAnotherTimeIsSparedAndTheJobIsPutBackAllTheSame()
    {
        var held = new HeldEncodeJobs();
        var strays = new ScriptedStrays { Answer = StrayFate.AnotherProgrammeHasThatId };
        EncodeJob job = Running(Another);
        held.Jobs.Add(job);

        EncodeRestartReport report = await Restart(held, strays).RecoverAsync(Cancel);

        Assert.Equal(0, report.Stopped);
        Assert.Equal(1, report.Spared);
        Assert.Equal(EncodeJobStatus.Queued, job.Status);
    }

    [Fact(DisplayName = "a running job that wrote no programme down asks nothing to be stopped")]
    public async Task ARunningJobThatWroteNoProgrammeDownAsksNothingToBeStopped()
    {
        var held = new HeldEncodeJobs();
        var strays = new ScriptedStrays();
        held.Jobs.Add(Running(null));

        EncodeRestartReport report = await Restart(held, strays).RecoverAsync(Cancel);

        Assert.Empty(strays.Asked);
        Assert.Equal(1, report.PutBack);
    }

    [Fact(DisplayName = "a job given up on its last attempt still has its programme stopped first")]
    public async Task AJobGivenUpStillHasItsProgrammeStopped()
    {
        var held = new HeldEncodeJobs();
        var strays = new ScriptedStrays();
        EncodeJob job = Running(Ffmpeg, attempt: 3);
        held.Jobs.Add(job);

        EncodeRestartReport report = await Restart(held, strays, mostAttempts: 3).RecoverAsync(Cancel);

        Assert.Equal([Ffmpeg], strays.Asked);
        Assert.Equal(1, report.GivenUp);
        Assert.Equal(1, report.Stopped);
        Assert.Equal(EncodeJobStatus.Failed, job.Status);
        Assert.Null(job.Programme);
    }

    [Fact]
    public async Task AJobGivenUpHasWhatItStillOwesARemovalForSwept()
    {
        using EncodeHarness harness = new();
        EncodeJob job = harness.Running();
        string work = harness.WorkFileOf(job, "half a picture");

        EncodeRestartReport report = await Restart(harness, mostAttempts: 1).RecoverAsync(Cancel);

        Assert.Equal(1, report.GivenUp);
        Assert.Equal(EncodeJobStatus.Failed, job.Status);
        Assert.False(File.Exists(work), "the work file of a job given up was left on the disk");
        Assert.Equal(EncodeScratchFate.Removed, Assert.Single(harness.Scratch.Files).Fate);
    }

    [Fact]
    public async Task AJobPutBackKeepsItsWorkFileOwedUntilItEnds()
    {
        using EncodeHarness harness = new();
        EncodeJob job = harness.Running();
        string work = harness.WorkFileOf(job, "half a picture");

        EncodeRestartReport report = await Restart(harness, mostAttempts: 3).RecoverAsync(Cancel);

        Assert.Equal(1, report.PutBack);
        Assert.True(File.Exists(work));
        Assert.True(Assert.Single(harness.Scratch.Files).IsOwedARemoval);
    }

    private static EncodeRestart Restart(EncodeHarness harness, int mostAttempts)
        => new(
            harness.Jobs,
            new ScriptedStrays(),
            harness.Cleaner,
            new EncodeSettings { MostAttempts = mostAttempts },
            new HandTurnedClock(new DateTimeOffset(Now)),
            NullLogger<EncodeRestart>.Instance);

    private static EncodeJob Running(RunningProgramme? programme, int attempt = 1)
        => EncodeJob.Rehydrate(
            EncodeJobId.New(),
            RecordingId.New(),
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            EncodeHarness.Primary,
            EncodeJobStatus.Running,
            attempt,
            EncodeHarness.Queued,
            EncodeHarness.Started,
            null,
            null,
            null,
            null,
            programme,
            null,
            null,
            null);

    private static EncodeRestart Restart(HeldEncodeJobs held, ScriptedStrays strays, int mostAttempts = 3)
        => new(
            held,
            strays,
            new EncodeScratchCleaner(
                new HeldEncodeScratch(),
                new EncodePlaces(new IntegritySettings(), new EncodeSettings()),
                new HandTurnedClock(new DateTimeOffset(Now)),
                NullLogger<EncodeScratchCleaner>.Instance),
            new EncodeSettings { MostAttempts = mostAttempts },
            new HandTurnedClock(new DateTimeOffset(Now)),
            NullLogger<EncodeRestart>.Instance);
}

internal sealed class ScriptedStrays : IStrayProgrammes
{
    public List<RunningProgramme> Asked { get; } = [];

    public StrayFate Answer { get; set; } = StrayFate.Stopped;

    public StrayFate Stop(RunningProgramme written)
    {
        Asked.Add(written);

        return Answer;
    }
}
