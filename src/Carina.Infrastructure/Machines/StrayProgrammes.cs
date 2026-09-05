using System.ComponentModel;
using System.Diagnostics;

using Carina.Domain.Machines;

namespace Carina.Infrastructure.Machines;

/// <summary>
/// Stops a programme an earlier process wrote down and did not live to stop. The id is looked up
/// and the programme found under it is the one written down only if it began when that one began,
/// within <see cref="Drift"/>: the start time is read from the kernel's own record of the process,
/// so a later programme that was handed the same id began later and is left alone. A programme
/// that is gone, or is gone by the time it is looked at, is reported as such rather than as
/// stopped. The wait after the kill reads the kernel's record too, because a programme whose
/// parent died with the last process is reaped by nobody this process knows.
/// </summary>
public sealed class StrayProgrammes(TimeSpan drift, TimeSpan patience) : IStrayProgrammes
{
    public static readonly TimeSpan Drift = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Glance = TimeSpan.FromMilliseconds(50);

    public StrayProgrammes()
        : this(Drift, Patience)
    {
    }

    public StrayFate Stop(RunningProgramme written)
    {
        ArgumentNullException.ThrowIfNull(written);

        Process found;

        try
        {
            found = Process.GetProcessById(written.ProcessId);
        }
        catch (ArgumentException)
        {
            return StrayFate.AlreadyGone;
        }

        using (found)
        {
            DateTime began;

            try
            {
                if (found.HasExited || IsGone(written.ProcessId))
                {
                    return StrayFate.AlreadyGone;
                }

                began = found.StartTime.ToUniversalTime();
            }
            catch (Exception gone) when (gone is InvalidOperationException or Win32Exception)
            {
                return StrayFate.AlreadyGone;
            }

            if (!written.IsTheSameAs(began, drift))
            {
                return StrayFate.AnotherProgrammeHasThatId;
            }

            try
            {
                found.Kill(entireProcessTree: true);
            }
            catch (Exception refused) when (refused is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                return IsGone(written.ProcessId) ? StrayFate.AlreadyGone : StrayFate.CouldNotBeStopped;
            }

            return WaitedOut(written.ProcessId) ? StrayFate.Stopped : StrayFate.CouldNotBeStopped;
        }
    }

    private bool WaitedOut(int processId)
    {
        var waited = Stopwatch.StartNew();

        while (!IsGone(processId))
        {
            if (waited.Elapsed >= patience)
            {
                return false;
            }

            Thread.Sleep(Glance);
        }

        return true;
    }

    /// <summary>
    /// Gone as the kernel sees it: no record under the id, or a record of a process that has
    /// exited and waits only to be reaped by a parent this process is not.
    /// </summary>
    internal static bool IsGone(int processId)
    {
        string record;

        try
        {
            record = File.ReadAllText($"/proc/{processId}/stat");
        }
        catch (Exception absent) when (absent is IOException or UnauthorizedAccessException)
        {
            return !Directory.Exists($"/proc/{processId}");
        }

        int afterName = record.LastIndexOf(')');

        if (afterName < 0 || afterName + 2 >= record.Length)
        {
            return false;
        }

        return record[afterName + 2] is 'Z' or 'X';
    }
}
