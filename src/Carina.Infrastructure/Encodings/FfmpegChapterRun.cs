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
/// One run of the programme that looks for the breaks.
/// </summary>
/// <remarks>
/// Both of its streams are read as they come and handed on one piece at a time, a line of words or,
/// when asked for, a whole picture; nothing is kept from either stream but how the run ended, and a
/// trailing part of a picture is not handed on. The programme is started yielding. A run that
/// outlives what it was allowed is stopped with its children and reported as having taken too long;
/// a stop the caller asked for is thrown. The programme's id and start are handed to the caller
/// before either stream is read, and a caller that cannot write them down stops the programme; one
/// already gone by then is not handed over.
/// </remarks>
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
        using CancellationTokenSource late = new(longest, clock);
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
