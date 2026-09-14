using System.Diagnostics;

using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Encodings;

public enum ChapterRunFault
{
    ProgrammeMissing = 1,

    TookTooLong = 2,
}

public sealed record ChapterRunOutcome(int? ExitCode, ChapterRunFault? Fault, string Complained)
{
    public bool Succeeded => Fault is null && ExitCode is 0;
}

/// <summary>
/// One run of the programme that looks for the breaks. Both of its streams are read a line at a
/// time and handed on as they come, one line at a time and never two at once, because a run that
/// is asked to talk says thousands of lines and holding them to read at the end would keep a
/// megabyte of another programme's words in memory and fill the pipe it is writing into while it
/// waited. Nothing is kept from either stream: the caller is handed the lines and this keeps only
/// how the run ended, so no word of the source's own can end up written down.
/// <para>
/// The programme is started yielding, at the lowest priority the machine has, because the machine
/// this runs on is recording. A run that outlives what it was allowed is stopped, children and
/// all, and said to have taken too long rather than throwing; a stop the caller asked for is
/// thrown, so the caller knows nothing was read.
/// </para>
/// </summary>
public static class FfmpegChapterRun
{
    public static async Task<ChapterRunOutcome> RunAsync(
        string programme,
        IReadOnlyList<string> arguments,
        Action<string> said,
        Action<string> complained,
        TimeSpan longest,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(said);
        ArgumentNullException.ThrowIfNull(complained);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(longest, TimeSpan.Zero);

        ProgrammeStart start = AnotherProgramme.Start(programme, arguments, ProgrammePriority.Yielding);

        if (start.Process is null)
        {
            return new ChapterRunOutcome(null, ChapterRunFault.ProgrammeMissing, start.Complained);
        }

        using Process running = start.Process;
        using var late = new CancellationTokenSource(longest, clock);
        using CancellationTokenRegistration stopWhenLate =
            late.Token.UnsafeRegister(_ => AnotherProgramme.GiveUpOn(running), null);
        using CancellationTokenRegistration stopWhenCancelled =
            cancellationToken.UnsafeRegister(_ => AnotherProgramme.GiveUpOn(running), null);

        var gate = new Lock();
        Task complaining = ReadAsync(running.StandardError, complained, gate);

        try
        {
            await ReadAsync(running.StandardOutput, said, gate);
            await complaining;
        }
        catch
        {
            AnotherProgramme.GiveUpOn(running);

            throw;
        }

        await running.WaitForExitAsync(CancellationToken.None);

        cancellationToken.ThrowIfCancellationRequested();

        return late.IsCancellationRequested
            ? new ChapterRunOutcome(null, ChapterRunFault.TookTooLong, string.Empty)
            : new ChapterRunOutcome(running.ExitCode, null, string.Empty);
    }

    private static async Task ReadAsync(StreamReader stream, Action<string> hand, Lock gate)
    {
        while (await stream.ReadLineAsync(CancellationToken.None) is { } line)
        {
            lock (gate)
            {
                hand(line);
            }
        }
    }
}
