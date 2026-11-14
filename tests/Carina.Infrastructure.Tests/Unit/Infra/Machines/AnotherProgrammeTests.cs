using System.Diagnostics;
using System.Runtime.Versioning;

using Carina.Domain.Machines;
using Carina.Infrastructure.Machines;
using Carina.Infrastructure.Tests.Fixtures.Integrity;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Unit.Infra.Machines;

[SupportedOSPlatform("linux")]
[Collection(ProcessEnvironmentCollection.Name)]
[Trait("Category", "Unit")]
public sealed class AnotherProgrammeTests : IDisposable
{
    private readonly TempTree tree = new();

    private readonly StandIns standIns = new();

    public void Dispose()
    {
        try
        {
            tree.Dispose();
        }
        finally
        {
            standIns.Dispose();
        }
    }

    [Fact(DisplayName = "a programme that started is handed back with its id and when it began, as the operating system has them")]
    public void AProgrammeThatStartedIsHandedBackWithItsIdAndStart()
    {
        ProgrammeStart start = AnotherProgramme.Start("sleep", ["30"]);
        using Process running = start.Process!;
        using Process asTheSystemHasIt = Process.GetProcessById(running.Id);

        RunningProgramme began = Assert.IsType<RunningProgramme>(start.Began);
        Assert.Equal(running.Id, began.ProcessId);
        Assert.Equal(asTheSystemHasIt.StartTime.ToUniversalTime(), began.StartedAt);
        Assert.Equal(DateTimeKind.Utc, began.StartedAt.Kind);
        AnotherProgramme.GiveUpOn(running);
    }

    [Fact(DisplayName = "a programme started yielding runs under nice at the lowest priority, keeps its own id, and is otherwise described the same")]
    public void AProgrammeStartedYieldingRunsUnderNiceAtTheLowestPriority()
    {
        ProcessStartInfo yielding = AnotherProgramme.Describe("ffmpeg", ["-version"], ProgrammePriority.Yielding);
        ProcessStartInfo ordinary = AnotherProgramme.Describe("ffmpeg", ["-version"], ProgrammePriority.Ordinary);

        Assert.Equal(AnotherProgramme.Located("nice"), yielding.FileName);
        Assert.Equal(["-n", "19", "ffmpeg", "-version"], yielding.ArgumentList);
        Assert.Equal(AnotherProgramme.Located("ffmpeg") ?? "ffmpeg", ordinary.FileName);
        Assert.Equal(["-version"], ordinary.ArgumentList);
        Assert.Equal(AnotherProgramme.SearchedIn, yielding.Environment["PATH"]);
        Assert.Single(yielding.Environment);
        Assert.Throws<ArgumentOutOfRangeException>(() => AnotherProgramme.Describe("ffmpeg", [], (ProgrammePriority)3));
    }

    [Fact(DisplayName = "a programme named without a path is started from the search path written here, rather than looked for again in the one this process inherited")]
    public void AProgrammeNamedWithoutAPathIsStartedFromTheSearchPathWrittenHere()
    {
        string sleep = SearchedFor("sleep");
        string nice = SearchedFor("nice");

        Assert.Equal(sleep, AnotherProgramme.Describe("sleep", ["1"]).FileName);
        Assert.Equal(nice, AnotherProgramme.Describe("sleep", ["1"], ProgrammePriority.Yielding).FileName);
        Assert.Equal(sleep, AnotherProgramme.Located(sleep));
        Assert.Null(AnotherProgramme.Located("no-such-programme-anywhere"));
    }

    [Fact(DisplayName = "a yielding programme is what the id names — nice gives way to the programme rather than sitting beside it")]
    public async Task AYieldingProgrammeIsWhatTheIdNames()
    {
        string script = Standing("echo $$; nice");

        ProgrammeStart start = AnotherProgramme.Start(script, [], ProgrammePriority.Yielding);
        using Process running = start.Process!;
        string said = await running.StandardOutput.ReadToEndAsync();
        await running.WaitForExitAsync();

        string[] lines = said.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(running.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), lines[0].Trim());
        Assert.Equal("19", lines[1].Trim());
    }

    [Fact(DisplayName = "a programme fed on its standard input can be started yielding, and reads what it is fed at the lowest priority")]
    public async Task AFedProgrammeCanBeStartedYielding()
    {
        string script = Standing("nice; cat");

        ProgrammeStart start = AnotherProgramme.StartFed(script, [], ProgrammePriority.Yielding);
        using Process running = start.Process!;
        await running.StandardInput.WriteAsync("fed");
        running.StandardInput.Close();
        string said = await running.StandardOutput.ReadToEndAsync();
        await running.WaitForExitAsync();

        Assert.Equal("19\nfed", said.Trim());
        Assert.Equal(running.Id, start.Began?.ProcessId);
    }

    [Fact(DisplayName = "a programme that is not on this machine is missing whether it is started yielding or not, and the note says so without the path")]
    public void AProgrammeNotOnThisMachineIsMissingEitherWay()
    {
        string absent = tree.Under("no-such-programme");

        ProgrammeStart yielding = AnotherProgramme.Start(absent, [], ProgrammePriority.Yielding);
        ProgrammeStart ordinary = AnotherProgramme.Start(absent, []);
        ProgrammeStart byName = AnotherProgramme.Start("no-such-programme-anywhere", [], ProgrammePriority.Yielding);
        ProgrammeStart ordinaryByName = AnotherProgramme.Start("no-such-programme-anywhere", []);
        ProgrammeStart fed = AnotherProgramme.StartFed(absent, [], ProgrammePriority.Yielding);

        Assert.Null(yielding.Process);
        Assert.Null(yielding.Began);
        Assert.Null(ordinary.Process);
        Assert.Null(byName.Process);
        Assert.Null(ordinaryByName.Process);
        Assert.Null(fed.Process);
        Assert.Contains("no such file on the searched path", ordinaryByName.Complained, StringComparison.Ordinal);
        Assert.Contains("could not be started", yielding.Complained, StringComparison.Ordinal);
        Assert.DoesNotContain(tree.Root, yielding.Complained, StringComparison.Ordinal);
        Assert.False(AnotherProgramme.IsOnThisMachine(absent));
        Assert.True(AnotherProgramme.IsOnThisMachine("sleep"));
    }

    [Fact(DisplayName = "a command is handed over as an array and never as one piece of text")]
    public void ACommandIsHandedOverAsAnArrayAndNeverAsOnePieceOfText()
    {
        ProcessStartInfo start = AnotherProgramme.Describe("ffmpeg", ["-i", "/srv/a b; rm -rf /.ts", "-vf", "scale=1280:720"]);

        Assert.Equal(string.Empty, start.Arguments);
        Assert.Equal(["-i", "/srv/a b; rm -rf /.ts", "-vf", "scale=1280:720"], start.ArgumentList);
        Assert.False(start.UseShellExecute);
    }

    [Fact]
    public async Task WhatAProgrammeSaidAndComplainedOfComeBackApart()
    {
        ProgrammeSaid said = await Saying(standIns.Script("printf 'out\\n'; printf 'err\\n' >&2; exit 0"));

        Assert.True(said.Ran);
        Assert.Equal(0, said.ExitCode);
        Assert.Equal("out\n", said.Said);
        Assert.Equal("err", said.Complained);
    }

    [Fact]
    public async Task AProgrammeThatRefusedComesBackWithItsCode()
    {
        ProgrammeSaid said = await Saying(standIns.Script("exit 234"));

        Assert.True(said.Ran);
        Assert.Equal(234, said.ExitCode);
    }

    [Fact]
    public async Task AProgrammeThatIsNotOnThisMachineIsNotAnExceptionToBeThrown()
    {
        ProgrammeSaid said = await Saying(standIns.Named("no-such-programme"));

        Assert.False(said.Ran);
        Assert.Equal(ProgrammeFault.ProgrammeMissing, said.Fault);
        Assert.Null(said.ExitCode);
        Assert.NotEmpty(said.Complained);
        Assert.DoesNotContain('/', said.Complained);
    }

    [Fact]
    public async Task AProgrammeThatWillNotStopIsGivenUpOnAndNothingIsLeftRunning()
    {
        string pids = standIns.Named("pids");
        TimeSpan longest = TimeSpan.FromMilliseconds(250);
        var clock = new HandTurnedClock();

        Task<ProgrammeSaid> saying = AnotherProgramme.SayAsync(
            standIns.Script($"echo $$ > {pids}; sleep 60 & echo $! >> {pids}; wait"),
            [],
            longest,
            clock,
            CancellationToken.None);

        await StandIns.WroteDown(pids, 2);
        clock.Turn(longest);

        ProgrammeSaid said = await saying;

        Assert.False(said.Ran);
        Assert.Equal(ProgrammeFault.TimedOut, said.Fault);
        Assert.True(await standIns.NothingIsLeftOf(StandIns.Pids(pids)));
    }

    [Fact]
    public async Task ACallerThatStopsWaitingStopsTheProgrammeToo()
    {
        string pids = standIns.Named("pids");

        using var calledOff = new CancellationTokenSource();

        Task<ProgrammeSaid> saying = AnotherProgramme.SayAsync(
            standIns.Script($"echo $$ > {pids}; sleep 60 & echo $! >> {pids}; wait"),
            [],
            TimeSpan.FromSeconds(30),
            TimeProvider.System,
            calledOff.Token);

        await StandIns.WroteDown(pids, 2);
        await calledOff.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saying);
        Assert.True(await standIns.NothingIsLeftOf(StandIns.Pids(pids)));
    }

    [Fact(DisplayName = "nothing this process was given is handed on to the one it starts")]
    public void NothingThisProcessWasGivenIsHandedOnToTheOneItStarts()
    {
        string? held = Environment.GetEnvironmentVariable("CARINA_DB_CONNECTION");
        Environment.SetEnvironmentVariable("CARINA_DB_CONNECTION", "Host=db;Password=hunter2");

        try
        {
            ProcessStartInfo start = AnotherProgramme.Describe("ffmpeg", []);

            Assert.DoesNotContain("CARINA_DB_CONNECTION", start.Environment.Keys, StringComparer.Ordinal);
            Assert.Equal(["PATH"], start.Environment.Keys.Order(StringComparer.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CARINA_DB_CONNECTION", held);
        }
    }

    [Fact(DisplayName = "the search path a started programme gets is written down, not inherited")]
    public void TheSearchPathAStartedProgrammeGetsIsWrittenDownNotInherited()
    {
        string? searched = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", "/somewhere/else");

        try
        {
            Assert.Equal(AnotherProgramme.SearchedIn, AnotherProgramme.Describe("ffmpeg", []).Environment["PATH"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", searched);
        }
    }

    private static string SearchedFor(string programme)
        => AnotherProgramme.SearchedIn
            .Split(':')
            .Select(directory => Path.Combine(directory, programme))
            .First(File.Exists);

    private string Standing(string script)
        => StandInProgramme.Written(tree.Under($"programme-{Guid.NewGuid():N}.sh"), script);

    private static Task<ProgrammeSaid> Saying(string programme, TimeSpan? longest = null)
        => AnotherProgramme.SayAsync(
            programme,
            [],
            longest ?? TimeSpan.FromSeconds(30),
            TimeProvider.System,
            CancellationToken.None);
}
