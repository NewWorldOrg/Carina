using System.Diagnostics;

using Carina.Domain.Machines;
using Carina.Infrastructure.Machines;
using Carina.Infrastructure.Tests.Integrity;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Machines;

public sealed class AnotherProgrammeTests : IDisposable
{
    private readonly TempTree tree = new();

    public void Dispose() => tree.Dispose();

    [Fact(DisplayName = "a programme that started is handed back with its id and when it began, as the operating system has them")]
    public void AProgrammeThatStartedIsHandedBackWithItsIdAndStart()
    {
        DateTime before = DateTime.UtcNow.AddSeconds(-2);

        ProgrammeStart start = AnotherProgramme.Start("sleep", ["30"]);
        using Process running = start.Process!;

        RunningProgramme began = Assert.IsType<RunningProgramme>(start.Began);
        Assert.Equal(running.Id, began.ProcessId);
        Assert.InRange(began.StartedAt, before, DateTime.UtcNow.AddSeconds(2));
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

    private static string SearchedFor(string programme)
        => AnotherProgramme.SearchedIn
            .Split(':')
            .Select(directory => Path.Combine(directory, programme))
            .First(File.Exists);

    private string Standing(string script)
        => StandInProgramme.Written(tree.Under($"programme-{Guid.NewGuid():N}.sh"), script);
}
