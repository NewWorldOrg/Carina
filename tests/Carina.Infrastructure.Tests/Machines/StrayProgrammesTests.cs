using System.Diagnostics;

using Carina.Domain.Machines;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Tests.Machines;

public sealed class StrayProgrammesTests
{
    private static readonly TimeSpan Drift = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact(DisplayName = "BR-ED2-011: a programme written down as it was started is found under its id, is the same programme, and is stopped")]
    public void AProgrammeWrittenDownAsItWasStartedIsStopped()
    {
        ProgrammeStart start = AnotherProgramme.Start("sleep", ["30"]);
        using Process running = start.Process!;
        RunningProgramme written = start.Began!;

        StrayFate fate = new StrayProgrammes(Drift, Patience).Stop(written);

        Assert.Equal(StrayFate.Stopped, fate);
        Assert.True(running.WaitForExit(5000), "the programme is gone");
    }

    [Fact(DisplayName = "BR-ED2-011: a programme under the written id that began at another time is somebody else's, and is left running")]
    public void AProgrammeThatBeganAtAnotherTimeIsLeftRunning()
    {
        ProgrammeStart start = AnotherProgramme.Start("sleep", ["30"]);
        using Process running = start.Process!;
        var writtenAsEarlier = new RunningProgramme(running.Id, start.Began!.StartedAt.AddHours(-1));

        StrayFate fate = new StrayProgrammes(Drift, Patience).Stop(writtenAsEarlier);

        Assert.Equal(StrayFate.AnotherProgrammeHasThatId, fate);
        Assert.False(running.HasExited, "the programme that was not ours still runs");
        AnotherProgramme.GiveUpOn(running);
    }

    [Fact(DisplayName = "BR-ED2-011: a programme that has already exited is already gone, whether or not its id has been handed on")]
    public void AProgrammeThatHasAlreadyExitedIsAlreadyGone()
    {
        ProgrammeStart start = AnotherProgramme.Start("sleep", ["30"]);
        using Process ran = start.Process!;
        RunningProgramme written = start.Began!;
        AnotherProgramme.GiveUpOn(ran);
        Assert.True(ran.WaitForExit(5000), "the programme is gone before it is looked for");

        StrayFate fate = new StrayProgrammes(Drift, Patience).Stop(written);

        Assert.Equal(StrayFate.AlreadyGone, fate);
    }

    [Fact(DisplayName = "BR-ED2-011: an id nothing runs under is already gone")]
    public void AnIdNothingRunsUnderIsAlreadyGone()
    {
        var written = new RunningProgramme(int.MaxValue - 7, DateTime.UtcNow);

        Assert.Equal(StrayFate.AlreadyGone, new StrayProgrammes(Drift, Patience).Stop(written));
    }

    [Fact(DisplayName = "BR-ED2-011: the start time the kernel keeps for a programme reads the same, within the drift allowed, however many times it is read")]
    public void TheStartTimeReadsTheSameWithinTheDriftHoweverManyTimesItIsRead()
    {
        ProgrammeStart start = AnotherProgramme.Start("sleep", ["30"]);
        using Process running = start.Process!;
        RunningProgramme written = start.Began!;

        for (int reading = 0; reading < 20; reading++)
        {
            using Process again = Process.GetProcessById(running.Id);

            Assert.True(written.IsTheSameAs(again.StartTime.ToUniversalTime(), Drift), $"reading {reading} drifted by {(again.StartTime.ToUniversalTime() - written.StartedAt).Duration()}");
        }

        AnotherProgramme.GiveUpOn(running);
    }
}
