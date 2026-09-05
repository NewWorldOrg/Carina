using System.Diagnostics;

using Carina.Domain.Encodings;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Tests.Integrity;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class FfmpegEncodeRunTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan Patient = TimeSpan.FromSeconds(20);

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
            told.Add,
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
            _ => { },
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
            _ => { },
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
            _ => { },
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
            _ => { },
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
            _ => { },
            TimeProvider.System,
            stopping.Token));

        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(15), $"stopped rather than waited for: {waited.Elapsed}");
        Assert.False(File.Exists(marker));
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
