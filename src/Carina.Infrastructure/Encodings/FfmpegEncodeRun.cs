using System.Diagnostics;

using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Encodings;

public enum EncodeRunFault
{
    ProgrammeMissing = 1,

    Stalled = 2,
}

public sealed record EncodeRunOutcome(int? ExitCode, EncodeRunFault? Fault, string Complained, EncodeProgress? Reached)
{
    public bool Succeeded => Fault is null && ExitCode is 0;
}

/// <summary>
/// One run of the encoder for one job. The progress ffmpeg writes is read as it comes and handed
/// on block by block; a run that reports nothing for longer than it is allowed is stopped and said
/// to have stalled, and a run cut short by the caller is stopped and left for the caller to deal
/// with. What was said on the error stream is kept as a note with the paths taken out.
/// </summary>
public static class FfmpegEncodeRun
{
    public static async Task<EncodeRunOutcome> RunAsync(
        string programme,
        IReadOnlyList<string> arguments,
        TimeSpan? whole,
        TimeSpan stalledAfter,
        Action<EncodeProgress> told,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(told);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stalledAfter, TimeSpan.Zero);

        ProgrammeStart start = AnotherProgramme.Start(programme, arguments);

        if (start.Process is null)
        {
            return new EncodeRunOutcome(null, EncodeRunFault.ProgrammeMissing, start.Complained, null);
        }

        using Process running = start.Process;

        Task<string> complaint = running.StandardError.ReadToEndAsync(CancellationToken.None);
        var reading = new FfmpegProgressReading(whole);
        EncodeProgress? reached = null;

        using var stall = new CancellationTokenSource(stalledAfter, clock);
        using CancellationTokenRegistration stopWhenStalled = stall.Token.UnsafeRegister(_ => AnotherProgramme.GiveUpOn(running), null);
        using CancellationTokenRegistration stopWhenCancelled = cancellationToken.UnsafeRegister(_ => AnotherProgramme.GiveUpOn(running), null);

        while (await running.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
        {
            stall.CancelAfter(stalledAfter);

            if (reading.Read(line) is { } progress)
            {
                reached = progress;
                told(progress);
            }
        }

        await running.WaitForExitAsync(CancellationToken.None);

        string complained = ProgrammeNote.Of(await complaint, ProgrammeNote.Longest);

        cancellationToken.ThrowIfCancellationRequested();

        return stall.IsCancellationRequested
            ? new EncodeRunOutcome(null, EncodeRunFault.Stalled, complained, reached)
            : new EncodeRunOutcome(running.ExitCode, null, complained, reached);
    }
}
