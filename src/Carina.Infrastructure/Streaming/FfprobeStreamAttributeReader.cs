using System.Diagnostics;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Streaming;

public sealed class FfprobeStreamAttributeReader(StreamAttributeSettings settings, TimeProvider clock)
    : IStreamAttributeReader
{
    public async Task<StreamAttributeReading> ReadAsync(StreamSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        FfprobeAnswer answer = await AskedAsync(FfprobeInvocation.Arguments(source), cancellationToken);

        if (answer.Fault is StreamProbeFault.Refused)
        {
            return StreamAttributeReading.Refused(answer.ExitCode!.Value, answer.Note);
        }

        return answer.Fault is { } fault
            ? StreamAttributeReading.Unanswered(fault, answer.Note)
            : FfprobeAttributes.Read(answer.Said);
    }

    public async Task<CarriedSounds> SoundsAsync(
        StreamSource source,
        ServiceId service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(service);

        FfprobeAnswer answer = await AskedAsync(FfprobeInvocation.Programmes(source), cancellationToken);

        return answer.Fault is null
            ? FfprobeSounds.Read(answer.Said, service)
            : CarriedSounds.Unread(answer.Note);
    }

    private async Task<FfprobeAnswer> AskedAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProgrammeStart start = AnotherProgramme.Start(settings.Programme, arguments);

        if (start.Process is not { } started)
        {
            return FfprobeAnswer.Unanswered(StreamProbeFault.ProgrammeMissing, start.Complained);
        }

        using Process running = started;

        Task<string> answer = running.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> complaint = running.StandardError.ReadToEndAsync(CancellationToken.None);

        using CancellationTokenSource deadline = new(settings.LongestRead, clock);
        using CancellationTokenSource waiting =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            await running.WaitForExitAsync(waiting.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            GiveUpOn(running);

            return FfprobeAnswer.Unanswered(
                StreamProbeFault.TimedOut,
                $"the programme was still reading the stream after {settings.LongestRead}");
        }
        catch (OperationCanceledException)
        {
            GiveUpOn(running);

            throw;
        }

        return running.ExitCode is 0
            ? FfprobeAnswer.Answered(await answer)
            : FfprobeAnswer.Refused(running.ExitCode, await complaint);
    }

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

    private readonly record struct FfprobeAnswer(string Said, StreamProbeFault? Fault, int? ExitCode, string Note)
    {
        public static FfprobeAnswer Answered(string said) => new(said, null, null, string.Empty);

        public static FfprobeAnswer Unanswered(StreamProbeFault fault, string note)
            => new(string.Empty, fault, null, note);

        public static FfprobeAnswer Refused(int exitCode, string note)
            => new(string.Empty, StreamProbeFault.Refused, exitCode, note);
    }
}
