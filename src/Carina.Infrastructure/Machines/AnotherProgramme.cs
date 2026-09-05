using System.ComponentModel;
using System.Diagnostics;

using Carina.Domain.Base;
using Carina.Domain.Machines;

namespace Carina.Infrastructure.Machines;

public sealed record ProgrammeSaid(int? ExitCode, ProgrammeFault? Fault, string Said, string Complained)
{
    public bool Ran => Fault is null;
}

/// <summary>
/// How this application starts another programme: the arguments go over as an array, no shell sees
/// them (BR-EV-002), and the environment is built here rather than inherited, so nothing this
/// process was handed — a database password among it — reaches the one it starts (BR-EV-003).
/// </summary>
public static class AnotherProgramme
{
    public const string SearchedIn = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";

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

    public static async Task<ProgrammeSaid> SayAsync(
        string programme,
        IReadOnlyList<string> arguments,
        TimeSpan longest,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clock);

        Process? started;

        try
        {
            started = Process.Start(Describe(programme, arguments));
        }
        catch (Win32Exception failure)
        {
            return Missing(programme, failure.Message);
        }

        if (started is null)
        {
            return Missing(programme, "it started no process of its own");
        }

        using Process running = started;

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

    private static ProgrammeSaid Missing(string programme, string why)
        => new(
            null,
            ProgrammeFault.ProgrammeMissing,
            string.Empty,
            ProgrammeNote.Of($"'{programme}' could not be started on this machine: {why}", ProgrammeNote.Longest));

    private static void GiveUpOn(Process running)
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
