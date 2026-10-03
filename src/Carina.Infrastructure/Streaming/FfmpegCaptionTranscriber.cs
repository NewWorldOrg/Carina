using System.Diagnostics;
using System.Globalization;

using Carina.Domain.Base;
using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.Machines;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// Takes the captions out of a recorded file with the drawing live viewing uses, run on its own with no
/// picture decoded, at the lowest scheduling priority, and keeps every change in memory.
/// </summary>
public sealed class FfmpegCaptionTranscriber(
    MachineSettings machine,
    CaptionSettings settings,
    IStreamAttributeReader attributes,
    TimeProvider clock) : ICaptionTranscriber
{
    public const string StartKey = "start_time";

    private const long Lift = (long)FfmpegCaptionInvocation.ClockLiftedBySeconds * LivePts.Hertz;

    public async Task<CaptionTranscription> TranscribeAsync(
        string source,
        ServiceId service,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(service);

        StreamSource file = new(source);
        StreamAttributeReading reading = await attributes.ReadAsync(file, cancellationToken);
        CaptionCanvas canvas = new(reading.Attributes.Size);
        ProgrammeStart start = AnotherProgramme.Start(
            machine.Programme,
            FfmpegCaptionInvocation.Arguments(service, reading.Attributes, file),
            ProgrammePriority.Yielding);

        if (start.Process is null)
        {
            return CaptionTranscription.Failed(CaptionFault.ProgrammeMissing, start.Complained);
        }

        using Process running = start.Process;
        Drawn drawn = await DrawnAsync(running, canvas, cancellationToken);

        if (drawn.TimedOut)
        {
            return CaptionTranscription.Failed(
                CaptionFault.TimedOut,
                string.Create(CultureInfo.InvariantCulture, $"it was still drawing after {settings.LongestTranscription}"));
        }

        if (running.ExitCode is not 0)
        {
            return FfmpegComplaints.RefusedForWantOfACaptionStream(drawn.Complained)
                ? CaptionTranscription.WithoutACaptionStream(drawn.Complained)
                : CaptionTranscription.Failed(
                    CaptionFault.Refused,
                    string.Create(CultureInfo.InvariantCulture, $"exited {running.ExitCode}: {drawn.Complained}"));
        }

        if (drawn.Fault is { } broken)
        {
            return CaptionTranscription.Failed(CaptionFault.PicturesUnreadable, broken.ToString());
        }

        if (drawn.Cues.Count is 0)
        {
            return CaptionTranscription.NothingShown();
        }

        ProgrammeSaid said = await AnotherProgramme.SayAsync(
            machine.Prober,
            FfprobeInvocation.Start(file),
            machine.LongestRead,
            clock,
            cancellationToken);

        return Begins(said) is { } begins
            ? CaptionTranscription.Transcribed(new CaptionRecord(canvas.Size.Width, canvas.Size.Height, begins, drawn.Cues))
            : CaptionTranscription.Failed(
                CaptionFault.ClockUnread,
                said.ExitCode is 0 ? $"the programme named no '{StartKey}' this could be read as where the file begins" : said.Complained);
    }

    private static TimeSpan? Begins(ProgrammeSaid said)
    {
        if (said.ExitCode is not 0)
        {
            return null;
        }

        string? named = said.Said
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith(StartKey + "=", StringComparison.Ordinal))
            .Select(line => line[(StartKey.Length + 1)..])
            .FirstOrDefault();

        return double.TryParse(named, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && double.IsFinite(seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static CaptionCue Cue(LivePts at, CaptionPicture? picture)
        => new(
            (long)at.Value - Lift,
            picture is null ? null : new CaptionPlacement(picture.Left, picture.Top, picture.Width, picture.Height, picture.Png));

    private async Task<Drawn> DrawnAsync(Process running, CaptionCanvas canvas, CancellationToken cancellationToken)
    {
        List<CaptionCue> cues = [];
        Task<string> complaint = running.StandardError.ReadToEndAsync(CancellationToken.None);

        using CancellationTokenSource deadline = new(settings.LongestTranscription, clock);
        using CancellationTokenSource waiting =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            CaptionFlowFault? fault = await CaptionFrames.DrawAsync(
                running.StandardOutput.BaseStream,
                canvas,
                CaptionClock.FollowedThrough,
                (at, picture) =>
                {
                    cues.Add(Cue(at, picture));

                    return true;
                },
                waiting.Token);

            await running.WaitForExitAsync(waiting.Token);

            return new Drawn(cues, fault, false, ProgrammeNote.Of(await complaint, ProgrammeNote.Longest));
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            AnotherProgramme.GiveUpOn(running);

            return new Drawn([], null, true, string.Empty);
        }
        catch (OperationCanceledException)
        {
            AnotherProgramme.GiveUpOn(running);

            throw;
        }
    }

    private sealed record Drawn(IReadOnlyList<CaptionCue> Cues, CaptionFlowFault? Fault, bool TimedOut, string Complained);
}
