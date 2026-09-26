using System.Diagnostics;

using Carina.Domain.Machines;
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
/// One run of the programme that looks for the breaks. Both of its streams are read as they come
/// and handed on one piece at a time and never two at once — a line of words, or a whole picture
/// when the run was asked to hand pictures over — because a run that is asked to talk says
/// thousands of lines and holding them to read at the end would keep a megabyte of another
/// programme's words in memory and fill the pipe it is writing into while it waited. Nothing is kept
/// from either stream: the caller is handed each piece and this keeps only how the run ended, so no
/// word of the source's own can end up written down. What is left at the end of the pictures that
/// makes less than a whole one is not handed on.
/// <para>
/// The programme is started yielding, at the lowest priority the machine has, because the machine
/// this runs on is recording. A run that outlives what it was allowed is stopped, children and
/// all, and said to have taken too long rather than throwing; a stop the caller asked for is
/// thrown, so the caller knows nothing was read.
/// </para>
/// <para>
/// Who the programme is — its id and when it began — is handed to the caller before either stream
/// is read, so that a process which dies mid-look leaves behind a programme the next one can find
/// and stop; a caller that cannot write it down stops the programme rather than run it unrecorded
///. One already gone by then is not handed over, there being nothing left of it to
/// stop.
/// </para>
/// </summary>
public static class FfmpegChapterRun
{
    public static Task<ChapterRunOutcome> RunAsync(
        string programme,
        IReadOnlyList<string> arguments,
        Action<string> said,
        Action<string> complained,
        TimeSpan longest,
        Func<RunningProgramme, Task> began,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(said);

        return RunAsync(
            programme,
            arguments,
            (output, gate) => ReadAsync(output, said, gate),
            complained,
            longest,
            began,
            clock,
            cancellationToken);
    }

    public static Task<ChapterRunOutcome> PicturedAsync(
        string programme,
        IReadOnlyList<string> arguments,
        int pictureBytes,
        Action<byte[]> pictured,
        Action<string> complained,
        TimeSpan longest,
        Func<RunningProgramme, Task> began,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pictured);
        ArgumentOutOfRangeException.ThrowIfLessThan(pictureBytes, 1);

        return RunAsync(
            programme,
            arguments,
            (output, gate) => PicturesAsync(output.BaseStream, pictureBytes, pictured, gate),
            complained,
            longest,
            began,
            clock,
            cancellationToken);
    }

    private static async Task<ChapterRunOutcome> RunAsync(
        string programme,
        IReadOnlyList<string> arguments,
        Func<StreamReader, Lock, Task> readOutput,
        Action<string> complained,
        TimeSpan longest,
        Func<RunningProgramme, Task> began,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(complained);
        ArgumentNullException.ThrowIfNull(began);
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

        if (start.Began is { } spawned)
        {
            try
            {
                await began(spawned);
            }
            catch
            {
                AnotherProgramme.GiveUpOn(running);

                throw;
            }
        }

        var gate = new Lock();
        Task complaining = ReadAsync(running.StandardError, complained, gate);

        try
        {
            await readOutput(running.StandardOutput, gate);
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

    private static async Task PicturesAsync(Stream stream, int bytes, Action<byte[]> hand, Lock gate)
    {
        byte[] picture = new byte[bytes];

        while (await stream.ReadAtLeastAsync(picture, bytes, throwOnEndOfStream: false, CancellationToken.None) == bytes)
        {
            lock (gate)
            {
                hand(picture);
            }
        }
    }
}
