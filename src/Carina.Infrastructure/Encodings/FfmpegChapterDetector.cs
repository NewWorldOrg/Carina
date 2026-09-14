using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Looks for the breaks in one source with the ffmpeg this image already carries, in two passes.
/// The first listens to the whole of the sound and writes down every stretch that went quiet,
/// decoding no picture at all; the second looks at six seconds of picture around each of those and
/// writes down where it went black and by how much it changed. That is what makes this affordable:
/// the whole length is heard, and only a few seconds either side of a candidate are seen. What the
/// two passes observed is handed to <see cref="ChapterGrid"/>, which decides — nothing is decided
/// here.
/// <para>
/// This answers, it does not fail. A programme that is not on this machine, one that refused, one
/// that outlived <see cref="Patience"/>, and a source measured against some other clock all come
/// back as a reading that could not be made, so an encode that would have run without anyone
/// looking is not failed by the looking. What is written down beside such a reading is built here
/// out of the exit code and the reason: not one word of what the programme said is kept, because
/// what it says carries the source it was reading.
/// </para>
/// <para>
/// How much of the machine the two passes may take is handed in rather than read here, so that the
/// looking and the encode that follows it are bounded by the one cap the operator holds
/// (BR-ED2-005).
/// </para>
/// </summary>
public sealed class FfmpegChapterDetector(
    MachineSettings machine,
    EncodeSettings settings,
    TimeProvider clock,
    TimeSpan patience) : IChapterDetector
{
    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    public FfmpegChapterDetector(MachineSettings machine, EncodeSettings settings, TimeProvider clock)
        : this(machine, settings, clock, Patience)
    {
    }

    public async Task<ChapterDetection> MarkAsync(
        string source,
        ServiceId service,
        EncodeTimeline timeline,
        int cores,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentOutOfRangeException.ThrowIfLessThan(cores, 1);

        if (timeline.Expected is not { } artefactLength || artefactLength <= TimeSpan.Zero)
        {
            return ChapterDetection.Unreadable(
                "nothing measured how long the source is, so no moment reported in it could be placed on the artefact");
        }

        DateTimeOffset began = clock.GetUtcNow();
        ChapterSettings asked = settings.Chapters;
        var heard = new ChapterLog();

        ChapterRunOutcome listened = await RunAsync(
            FfmpegChapterInvocation.Listening(source, service, cores, asked),
            heard,
            began,
            cancellationToken);

        if (WhyNothingWasRead(listened, "listening to the whole of the sound") is { } deaf)
        {
            return ChapterDetection.Unreadable(deaf);
        }

        List<ChapterSpan> silences = [];
        int outOfReach = 0;

        foreach (ChapterSpan quiet in heard.Silences)
        {
            if (Placed(quiet, timeline, artefactLength) is { } within)
            {
                silences.Add(within);

                continue;
            }

            outOfReach++;
        }

        if (ChapterClock.TooMuchOutOfReach(outOfReach, heard.Silences.Count))
        {
            return ChapterDetection.Unreadable(string.Create(
                CultureInfo.InvariantCulture,
                $"{outOfReach} of the {heard.Silences.Count} quiet stretches reported fall outside the artefact, so what they were reported against is not the clock they were read on"));
        }

        var seen = new ChapterLog();

        foreach (ChapterSpan quiet in silences)
        {
            TimeSpan middle = quiet.Starts + ((quiet.Ends - quiet.Starts) / 2) + timeline.HeadSkip;

            ChapterRunOutcome peeked = await RunAsync(
                FfmpegChapterInvocation.Peeking(source, service, cores, middle, asked),
                seen,
                began,
                cancellationToken);

            if (WhyNothingWasRead(peeked, "looking at the picture around a quiet stretch") is { } blind)
            {
                return ChapterDetection.Unreadable(blind);
            }
        }

        List<ChapterSpan> blacks = [];

        foreach (ChapterSpan dark in seen.Blacks)
        {
            if (Placed(dark, timeline, artefactLength) is { } within)
            {
                blacks.Add(within);
            }
        }

        List<ChapterScene> changes = [];

        foreach (ChapterScene change in seen.Scenes)
        {
            if (ChapterClock.OnTheArtefact(change.At, timeline.SourceStart, timeline.HeadSkip, artefactLength) is { } at)
            {
                changes.Add(new ChapterScene(at, change.Score));
            }
        }

        var evidence = new ChapterEvidence { Silences = silences, Blacks = blacks, Scenes = changes };

        return ChapterGrid.Mark(evidence, artefactLength, asked);
    }

    private static ChapterSpan? Placed(ChapterSpan reported, EncodeTimeline timeline, TimeSpan artefactLength)
        => ChapterClock.OnTheArtefact(reported, timeline.SourceStart, timeline.HeadSkip, artefactLength);

    private string? WhyNothingWasRead(ChapterRunOutcome ran, string doing)
        => ran.Fault switch
        {
            ChapterRunFault.ProgrammeMissing => $"nothing looked, {doing} being beyond this machine: {ran.Complained}",
            ChapterRunFault.TookTooLong => string.Create(
                CultureInfo.InvariantCulture,
                $"{doing} was still going after {patience:c} and was stopped"),
            _ => ran.Succeeded
                ? null
                : string.Create(CultureInfo.InvariantCulture, $"the programme exited {ran.ExitCode} while {doing}"),
        };

    private async Task<ChapterRunOutcome> RunAsync(
        IReadOnlyList<string> arguments,
        ChapterLog log,
        DateTimeOffset began,
        CancellationToken cancellationToken)
    {
        TimeSpan left = patience - (clock.GetUtcNow() - began);

        return left <= TimeSpan.Zero
            ? new ChapterRunOutcome(null, ChapterRunFault.TookTooLong, string.Empty)
            : await FfmpegChapterRun.RunAsync(
                machine.Programme,
                arguments,
                log.Said,
                log.Complained,
                left,
                clock,
                cancellationToken);
    }
}
