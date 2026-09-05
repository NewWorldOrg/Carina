using System.Diagnostics;

using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Tests.Integrity;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class FfmpegEncodeRunTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan Patient = TimeSpan.FromSeconds(20);

    private static readonly Func<RunningProgramme, Task> Nobody = _ => Task.CompletedTask;

    private static readonly Func<EncodeProgress, Task> Nothing = _ => Task.CompletedTask;

    private readonly TempTree tree = new();

    public void Dispose() => tree.Dispose();

    [Fact(DisplayName = "BR-ED2-013: every progress block the programme writes is handed on as it comes, and the last one is kept with the exit code")]
    public async Task EveryProgressBlockIsHandedOnAsItComes()
    {
        List<EncodeProgress> told = [];

        EncodeRunOutcome ran = await FfmpegEncodeRun.RunAsync(
            Standing("""
                printf 'out_time_us=2000000\nspeed=1.5x\nprogress=continue\n'
                printf 'out_time_us=4000000\nspeed=1.5x\nprogress=end\n'
                echo 'a complaint about the source at /srv/somewhere/x.ts' >&2
                exit 0
                """),
            [],
            TimeSpan.FromSeconds(4),
            Patient,
            Nobody,
            progress =>
            {
                told.Add(progress);

                return Task.CompletedTask;
            },
            TimeProvider.System,
            Cancel);

        Assert.True(ran.Succeeded);
        Assert.Equal(0, ran.ExitCode);
        Assert.Equal([0.5, 1], told.Select(progress => progress.Portion));
        Assert.Same(told[^1], ran.Reached);
        Assert.Equal("a complaint about the source at …", ran.Complained);
    }

    [Fact(DisplayName = "BR-ED2-012: a non-zero exit comes back as the code and the complaint, not as an exception")]
    public async Task ANonZeroExitComesBackAsTheCodeAndTheComplaint()
    {
        EncodeRunOutcome ran = await FfmpegEncodeRun.RunAsync(
            Standing("echo refused >&2; exit 3"),
            [],
            null,
            Patient,
            Nobody,
            Nothing,
            TimeProvider.System,
            Cancel);

        Assert.False(ran.Succeeded);
        Assert.Equal(3, ran.ExitCode);
        Assert.Null(ran.Fault);
        Assert.Equal("refused", ran.Complained);
        Assert.Null(ran.Reached);
    }

    [Fact(DisplayName = "BR-EV-004: a programme that is not on this machine is a fault of its own, named without the path")]
    public async Task AProgrammeNotOnThisMachineIsAFaultOfItsOwn()
    {
        EncodeRunOutcome ran = await FfmpegEncodeRun.RunAsync(
            tree.Under("no-such-programme"),
            [],
            null,
            Patient,
            Nobody,
            Nothing,
            TimeProvider.System,
            Cancel);

        Assert.Equal(EncodeRunFault.ProgrammeMissing, ran.Fault);
        Assert.Null(ran.ExitCode);
        Assert.DoesNotContain(tree.Root, ran.Complained, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ED2-014: a programme that goes quiet for longer than allowed is stopped, children and all, and said to have stalled")]
    public async Task AProgrammeThatGoesQuietIsStoppedAndSaidToHaveStalled()
    {
        string marker = tree.Under("woke");
        Stopwatch waited = Stopwatch.StartNew();

        EncodeRunOutcome ran = await FfmpegEncodeRun.RunAsync(
            Standing($"""
                printf 'out_time_us=1000000\nprogress=continue\n'
                sleep 30
                printf woke > "{marker}"
                """),
            [],
            null,
            TimeSpan.FromMilliseconds(300),
            Nobody,
            Nothing,
            TimeProvider.System,
            Cancel);

        Assert.Equal(EncodeRunFault.Stalled, ran.Fault);
        Assert.Null(ran.ExitCode);
        Assert.NotNull(ran.Reached);
        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(15), $"stopped rather than waited for: {waited.Elapsed}");
        Assert.False(File.Exists(marker));
    }

    [Fact(DisplayName = "BR-ED2-014: a programme that keeps reporting is never taken for stalled, however long it runs")]
    public async Task AProgrammeThatKeepsReportingIsNeverTakenForStalled()
    {
        EncodeRunOutcome ran = await FfmpegEncodeRun.RunAsync(
            Standing("""
                for step in 1 2 3 4 5 6; do
                    printf 'out_time_us=%d\nprogress=continue\n' "$step"
                    sleep 0.2
                done
                printf 'progress=end\n'
                """),
            [],
            null,
            TimeSpan.FromMilliseconds(600),
            Nobody,
            Nothing,
            TimeProvider.System,
            Cancel);

        Assert.True(ran.Succeeded, ran.Fault?.ToString());
    }

    [Fact(DisplayName = "BR-ED2-011: a stop asked for by the caller stops the programme and is thrown, so the caller knows nothing ended")]
    public async Task AStopAskedForByTheCallerStopsTheProgrammeAndIsThrown()
    {
        using var stopping = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        string marker = tree.Under("woke");
        Stopwatch waited = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FfmpegEncodeRun.RunAsync(
            Standing($"""
                printf 'out_time_us=1000000\nprogress=continue\n'
                sleep 30
                printf woke > "{marker}"
                """),
            [],
            null,
            Patient,
            Nobody,
            Nothing,
            TimeProvider.System,
            stopping.Token));

        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(15), $"stopped rather than waited for: {waited.Elapsed}");
        Assert.False(File.Exists(marker));
    }

    [Fact(DisplayName = "BR-ED2-011: who the programme is — its id and when it began — is handed over before a line of its progress is read, and it is the programme that was started")]
    public async Task WhoTheProgrammeIsIsHandedOverBeforeItsProgressIsRead()
    {
        RunningProgramme? began = null;
        bool progressCameFirst = false;
        DateTime before = DateTime.UtcNow.AddSeconds(-2);

        EncodeRunOutcome ran = await FfmpegEncodeRun.RunAsync(
            Standing("""
                echo $$ > "$0.pid"
                printf 'out_time_us=1000000\nprogress=end\n'
                """),
            [],
            null,
            Patient,
            spawned =>
            {
                began = spawned;

                return Task.CompletedTask;
            },
            _ =>
            {
                progressCameFirst = began is null;

                return Task.CompletedTask;
            },
            TimeProvider.System,
            Cancel);

        Assert.True(ran.Succeeded);
        Assert.NotNull(began);
        Assert.False(progressCameFirst, "the programme was identified before its progress was read");
        Assert.InRange(began.StartedAt, before, DateTime.UtcNow.AddSeconds(2));
        string wroteItsOwnId = Directory.EnumerateFiles(tree.Root, "*.pid").Single();
        Assert.Equal(began.ProcessId, int.Parse(File.ReadAllText(wroteItsOwnId).Trim(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact(DisplayName = "BR-ED2-011: a programme whose identity cannot be written down is stopped rather than run unrecorded")]
    public async Task AProgrammeWhoseIdentityCannotBeWrittenDownIsStopped()
    {
        string marker = tree.Under("woke");
        Stopwatch waited = Stopwatch.StartNew();

        await Assert.ThrowsAsync<IOException>(() => FfmpegEncodeRun.RunAsync(
            Standing($"""
                sleep 30
                printf woke > "{marker}"
                """),
            [],
            null,
            Patient,
            _ => throw new IOException("the ledger is away"),
            Nothing,
            TimeProvider.System,
            Cancel));

        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(15), $"stopped rather than waited for: {waited.Elapsed}");
        await Task.Delay(200);
        Assert.False(File.Exists(marker));
    }

    [Fact(DisplayName = "BR-ED2-005: the programme runs yielding, at the lowest priority the scheduler has, from its first instruction")]
    public async Task TheProgrammeRunsYieldingFromItsFirstInstruction()
    {
        string niceness = tree.Under("niceness");

        EncodeRunOutcome ran = await FfmpegEncodeRun.RunAsync(
            Standing($"nice > \"{niceness}\""),
            [],
            null,
            Patient,
            Nobody,
            Nothing,
            TimeProvider.System,
            Cancel);

        Assert.True(ran.Succeeded, ran.Complained);
        Assert.Equal("19", File.ReadAllText(niceness).Trim());
    }

    [Fact(DisplayName = "BR-ED2-014: a programme that keeps reporting the same place is making no headway, and is stopped as stalled like one that says nothing")]
    public async Task AProgrammeThatKeepsReportingTheSamePlaceIsStalled()
    {
        Stopwatch waited = Stopwatch.StartNew();

        EncodeRunOutcome ran = await FfmpegEncodeRun.RunAsync(
            Standing("""
                for step in 1 2 3 4 5 6 7 8 9 10; do
                    printf 'out_time_us=1000000\nprogress=continue\n'
                    sleep 0.2
                done
                printf 'progress=end\n'
                """),
            [],
            null,
            TimeSpan.FromMilliseconds(700),
            Nobody,
            Nothing,
            TimeProvider.System,
            Cancel);

        Assert.Equal(EncodeRunFault.Stalled, ran.Fault);
        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(2), $"stopped once the same place had been reported for long enough: {waited.Elapsed}");
    }

    private string Standing(string script)
    {
        string path = tree.Under($"programme-{Guid.NewGuid():N}.sh");
        File.WriteAllText(path, "#!/bin/sh\n" + script + "\n");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}
