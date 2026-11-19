using System.Diagnostics;

using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
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

public static class FfmpegEncodeRun
{
    public static async Task<EncodeRunOutcome> RunAsync(
        string programme,
        IReadOnlyList<string> arguments,
        TimeSpan? whole,
        TimeSpan stalledAfter,
        Func<RunningProgramme, Task> began,
        Func<EncodeProgress, Task> told,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(began);
        ArgumentNullException.ThrowIfNull(told);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stalledAfter, TimeSpan.Zero);

        ProgrammeStart start = AnotherProgramme.Start(programme, arguments, ProgrammePriority.Yielding);

        if (start.Process is null)
        {
            return new EncodeRunOutcome(null, EncodeRunFault.ProgrammeMissing, start.Complained, null);
        }

        using Process running = start.Process;

        Task<string> complaint = running.StandardError.ReadToEndAsync(CancellationToken.None);
        var reading = new FfmpegProgressReading(whole);
        EncodeProgress? reached = null;
        TimeSpan farthest = TimeSpan.Zero;

        using CancellationTokenSource stall = new(stalledAfter, clock);
        using CancellationTokenRegistration stopWhenStalled = stall.Token.UnsafeRegister(_ => AnotherProgramme.GiveUpOn(running), null);
        using CancellationTokenRegistration stopWhenCancelled = cancellationToken.UnsafeRegister(_ => AnotherProgramme.GiveUpOn(running), null);

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

        try
        {
            while (await running.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
            {
                if (reading.Read(line) is not { } progress)
                {
                    continue;
                }

                if (progress.Ended || progress.Reached > farthest)
                {
                    farthest = progress.Reached;
                    stall.CancelAfter(stalledAfter);
                }

                reached = progress;
                await told(progress);
            }
        }
        catch
        {
            AnotherProgramme.GiveUpOn(running);

            throw;
        }

        await running.WaitForExitAsync(CancellationToken.None);

        string complained = ProgrammeNote.Of(await complaint, ProgrammeNote.Longest);

        cancellationToken.ThrowIfCancellationRequested();

        return stall.IsCancellationRequested
            ? new EncodeRunOutcome(null, EncodeRunFault.Stalled, complained, reached)
            : new EncodeRunOutcome(running.ExitCode, null, complained, reached);
    }
}
