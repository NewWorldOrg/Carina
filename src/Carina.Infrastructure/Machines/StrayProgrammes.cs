using System.ComponentModel;
using System.Diagnostics;

using Carina.Domain.Machines;

namespace Carina.Infrastructure.Machines;

/// <summary>
/// Stops a programme an earlier process wrote down and did not live to stop. The programme under the
/// recorded id is stopped only if, by the kernel's own record, it began when the recorded one began,
/// within <see cref="Drift"/>. A programme that is gone, or is gone by the time it is looked at, is
/// reported as such. The wait after the kill reads the kernel's record.
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
        Stopwatch waited = Stopwatch.StartNew();

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
    /// Whether the process is gone as the kernel sees it: no record under the id, or a record of a
    /// process that has exited and waits to be reaped.
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
