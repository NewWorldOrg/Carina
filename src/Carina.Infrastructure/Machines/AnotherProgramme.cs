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

public sealed record ProgrammeStart(Process? Process, RunningProgramme? Began, string Complained)
{
    public bool Started => Process is not null;
}

public static class AnotherProgramme
{
    public const string SearchedIn = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";

    public const string Nice = "nice";

    public const string LowestPriority = "19";

    public static ProcessStartInfo Describe(string programme, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(programme);
        ArgumentNullException.ThrowIfNull(arguments);

        var start = new ProcessStartInfo(Located(programme) ?? programme)
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
        => Start(programme, priority, () => Describe(programme, arguments, priority));

    public static ProgrammeStart StartFed(string programme, IReadOnlyList<string> arguments)
        => StartFed(programme, arguments, ProgrammePriority.Ordinary);

    public static ProgrammeStart StartFed(string programme, IReadOnlyList<string> arguments, ProgrammePriority priority)
        => Start(programme, priority, () => Fed(Describe(programme, arguments, priority)));

    private static ProgrammeStart Start(string programme, ProgrammePriority priority, Func<ProcessStartInfo> described)
    {
        ArgumentException.ThrowIfNullOrEmpty(programme);

        if (NotOnThisMachine(programme, priority) is { } absent)
        {
            return new ProgrammeStart(null, null, Missing(absent, "no such file on the searched path").Complained);
        }

        Process? started;

        try
        {
            started = Process.Start(described());
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

        using CancellationTokenSource deadline = new(longest, clock);
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

    public static bool IsOnThisMachine(string programme) => Located(programme) is not null;

    public static string? Located(string programme)
    {
        ArgumentException.ThrowIfNullOrEmpty(programme);

        if (programme.Contains('/', StringComparison.Ordinal))
        {
            return File.Exists(programme) ? programme : null;
        }

        return SearchedIn
            .Split(':')
            .Select(directory => Path.Combine(directory, programme))
            .FirstOrDefault(File.Exists);
    }

    private static ProcessStartInfo Fed(ProcessStartInfo start)
    {
        start.RedirectStandardInput = true;

        return start;
    }

    private static string? NotOnThisMachine(string programme, ProgrammePriority priority)
    {
        if (!IsOnThisMachine(programme))
        {
            return programme;
        }

        return priority is ProgrammePriority.Yielding && !IsOnThisMachine(Nice) ? Nice : null;
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
