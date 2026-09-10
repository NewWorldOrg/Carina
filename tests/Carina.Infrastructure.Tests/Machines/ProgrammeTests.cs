using System.Diagnostics;
using System.Runtime.Versioning;

using Carina.Domain.Machines;
using Carina.Infrastructure.Machines;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Machines;

[SupportedOSPlatform("linux")]
public sealed class ProgrammeTests : IDisposable
{
    private readonly StandIns standIns = new();

    public void Dispose() => standIns.Dispose();

    [Fact(DisplayName = "BR-EV-002: a command is handed over as an array and never as one piece of text")]
    public void ACommandIsHandedOverAsAnArrayAndNeverAsOnePieceOfText()
    {
        ProcessStartInfo start = AnotherProgramme.Describe("ffmpeg", ["-i", "/srv/a b; rm -rf /.ts", "-vf", "scale=1280:720"]);

        Assert.Equal(string.Empty, start.Arguments);
        Assert.Equal(["-i", "/srv/a b; rm -rf /.ts", "-vf", "scale=1280:720"], start.ArgumentList);
        Assert.False(start.UseShellExecute);
    }

    [Fact(DisplayName = "BR-EV-003: nothing this process was given is handed on to the one it starts")]
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

    [Fact(DisplayName = "BR-EV-003: the search path a started programme gets is written down, not inherited")]
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

    private static Task<ProgrammeSaid> Saying(string programme, TimeSpan? longest = null)
        => AnotherProgramme.SayAsync(
            programme,
            [],
            longest ?? TimeSpan.FromSeconds(30),
            TimeProvider.System,
            CancellationToken.None);
}
