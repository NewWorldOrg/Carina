using System.ComponentModel;
using System.Diagnostics;

using Carina.Domain.Base;
using Carina.Domain.Machines;

namespace Carina.Infrastructure.Machines;

public enum ProgrammePriority
{
    Ordinary = 1,

    Yielding = 2,
}

public sealed record ProgrammeSaid(int? ExitCode, ProgrammeFault? Fault, string Said, string Complained)
{
    public bool Ran => Fault is null;
}

/// <summary>
/// A programme that was asked to start: the process when it did, and how the operating system
/// knows it, so that whoever started it can write that down; otherwise the reason it could not be
/// started on this machine, with any path on it already taken out. A programme that started and
/// had already exited by the time it was looked at has a process and no identity: the kernel's
/// record of it went with it, and nothing of it is left to write down or to stop.
/// </summary>
public sealed record ProgrammeStart(Process? Process, RunningProgramme? Began, string Complained)
{
    public bool Started => Process is not null;
}

/// <summary>
/// How this application starts another programme: the arguments go over as an array, no shell sees
/// them (BR-EV-002), and the environment is built here rather than inherited, so nothing this
/// process was handed — a database password among it — reaches the one it starts (BR-EV-003).
/// A programme started yielding runs at the lowest scheduling priority from its first instruction,
/// under <c>nice</c>, so that whatever else this machine is doing is served first (BR-ED2-005).
/// </summary>
public static class AnotherProgramme
{
    public const string SearchedIn = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";

    public const string Nice = "nice";

    public const string LowestPriority = "19";

    public static ProcessStartInfo Describe(string programme, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(programme);
        ArgumentNullException.ThrowIfNull(arguments);

        var start = new ProcessStartInfo(programme)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.Environment.Clear();
        start.Environment["PATH"] = SearchedIn;

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    public static ProcessStartInfo Describe(string programme, IReadOnlyList<string> arguments, ProgrammePriority priority)
    {
        ArgumentException.ThrowIfNullOrEmpty(programme);
        ArgumentNullException.ThrowIfNull(arguments);

        return priority switch
        {
            ProgrammePriority.Ordinary => Describe(programme, arguments),
            ProgrammePriority.Yielding => Describe(Nice, ["-n", LowestPriority, programme, .. arguments]),
            _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "A programme runs at one of the two priorities."),
        };
    }

    public static ProgrammeStart Start(string programme, IReadOnlyList<string> arguments)
        => Start(programme, arguments, ProgrammePriority.Ordinary);

    public static ProgrammeStart Start(string programme, IReadOnlyList<string> arguments, ProgrammePriority priority)
    {
        ArgumentException.ThrowIfNullOrEmpty(programme);

        if (priority is ProgrammePriority.Yielding && !IsOnThisMachine(programme))
        {
            return new ProgrammeStart(null, null, Missing(programme, "no such file on the searched path").Complained);
        }

        Process? started;

        try
        {
            started = Process.Start(Describe(programme, arguments, priority));
        }
        catch (Win32Exception failure)
        {
            return new ProgrammeStart(null, null, Missing(programme, failure.Message).Complained);
        }

        return started is null
            ? new ProgrammeStart(null, null, Missing(programme, "it started no process of its own").Complained)
            : new ProgrammeStart(started, Identify(started), string.Empty);
    }

    public static async Task<ProgrammeSaid> SayAsync(
        string programme,
        IReadOnlyList<string> arguments,
        TimeSpan longest,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clock);

        ProgrammeStart start = Start(programme, arguments);

        if (start.Process is null)
        {
            return new ProgrammeSaid(null, ProgrammeFault.ProgrammeMissing, string.Empty, start.Complained);
        }

        using Process running = start.Process;

        Task<string> answer = running.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> complaint = running.StandardError.ReadToEndAsync(CancellationToken.None);

        using var deadline = new CancellationTokenSource(longest, clock);
        using CancellationTokenSource waiting =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            await running.WaitForExitAsync(waiting.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            GiveUpOn(running);

            return new ProgrammeSaid(
                null,
                ProgrammeFault.TimedOut,
                string.Empty,
                $"it was still running after {longest}");
        }
        catch (OperationCanceledException)
        {
            GiveUpOn(running);

            throw;
        }

        return new ProgrammeSaid(running.ExitCode, null, await answer, ProgrammeNote.Of(await complaint, ProgrammeNote.Longest));
    }

    /// <summary>
    /// Whether the programme would be found the way the process would find it: as the path it is,
    /// or by name in the one search path a started programme is given. Asked before wrapping a
    /// programme in <c>nice</c>, because <c>nice</c> starts either way and only then finds nothing.
    /// </summary>
    public static bool IsOnThisMachine(string programme)
    {
        ArgumentException.ThrowIfNullOrEmpty(programme);

        return programme.Contains('/', StringComparison.Ordinal)
            ? File.Exists(programme)
            : SearchedIn.Split(':').Any(directory => File.Exists(Path.Combine(directory, programme)));
    }

    private static RunningProgramme? Identify(Process started)
    {
        try
        {
            return new RunningProgramme(started.Id, started.StartTime.ToUniversalTime());
        }
        catch (Exception gone) when (gone is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    private static ProgrammeSaid Missing(string programme, string why)
        => new(
            null,
            ProgrammeFault.ProgrammeMissing,
            string.Empty,
            ProgrammeNote.Of($"'{programme}' could not be started on this machine: {why}", ProgrammeNote.Longest));

    public static void GiveUpOn(Process running)
    {
        try
        {
            running.Kill(entireProcessTree: true);
        }
        catch (Exception gone) when (gone is InvalidOperationException or NotSupportedException)
        {
            return;
        }
    }
}
