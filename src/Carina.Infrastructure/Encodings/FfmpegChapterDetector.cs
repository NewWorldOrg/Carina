using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Looks for the breaks in one source with ffmpeg, in three passes, and hands what they observed to
/// <see cref="ChapterGrid"/>.
/// </summary>
/// <remarks>
/// The first pass listens to the whole of the sound for quiet stretches; the second looks at six
/// seconds of picture around each quiet stretch, longest first and at most
/// <see cref="MostLooksPerMark"/> for each mark allowed, for where it went black and how much it
/// changed; the third watches key frames, one a second, for the station's watermark, only in what
/// is left of <see cref="Patience"/>, and only when told to.
/// <para>
/// The watermark learned from this source is handed back beside the reading; this source is judged
/// only by a watermark learned ahead of it.
/// </para>
/// <para>
/// It answers rather than fails. A missing programme, a refusal while listening, running out of
/// <see cref="Patience"/> before any sound was read, and a source on some other clock come back as
/// a reading that could not be made, noted from the exit code and the reason only. A picture look
/// or watermark watch that falls short leaves the reading made from what was gathered, and the
/// reading says so.
/// </para>
/// <para>
/// How much of the machine the passes may take is handed in, and every programme a pass starts is
/// handed to the caller before it is read from.
/// </para>
/// </remarks>
public sealed class FfmpegChapterDetector(
    MachineSettings machine,
    EncodeSettings settings,
    TimeProvider clock,
    TimeSpan patience) : IChapterDetector
{
    /// <summary>
    /// How many quiet stretches the picture is looked at around, as a multiple of the marks a reading is
    /// allowed to put in.
    /// </summary>
    public const int MostLooksPerMark = 4;

    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    public FfmpegChapterDetector(MachineSettings machine, EncodeSettings settings, TimeProvider clock)
        : this(machine, settings, clock, Patience)
    {
    }

    public ChapterDetectorName Name => ChapterDetectorName.Ffmpeg;

    public async Task<ChapterDetection> MarkAsync(
        string source,
        ServiceId service,
        EncodeTimeline timeline,
        WatermarkMask? learnedAhead,
        int cores,
        Func<RunningProgramme, Task> began,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentOutOfRangeException.ThrowIfLessThan(cores, 1);
        ArgumentNullException.ThrowIfNull(began);

        if (timeline.Expected is not { } artefactLength || artefactLength <= TimeSpan.Zero)
        {
            return ChapterDetection.Unreadable(
                "nothing measured how long the source is, so no moment reported in it could be placed on the artefact");
        }

        DateTimeOffset from = clock.GetUtcNow();
        ChapterSettings asked = settings.Chapters;
        var heard = new ChapterLog();

        ChapterRunOutcome listened = await RunAsync(
            FfmpegChapterInvocation.Listening(source, service, cores, asked),
            heard,
            from,
            began,
            cancellationToken);

        if (WhyNothingWasReadAtAll(listened, "listening to the whole of the sound") is { } deaf)
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

        List<string> asides = [];
        var seen = new ChapterLog();
        int mostToLookAt = MostLooksPerMark * asked.MostChapters;
        int lookedThrough = 0;
        int refused = 0;

        foreach (ChapterSpan quiet in Ranked(silences))
        {
            if (lookedThrough + refused >= mostToLookAt)
            {
                asides.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"only the {mostToLookAt} longest of the {silences.Count} quiet stretches were looked at"));

                break;
            }

            TimeSpan middle = quiet.Starts + ((quiet.Ends - quiet.Starts) / 2) + timeline.HeadSkip;

            ChapterRunOutcome peeked = await RunAsync(
                FfmpegChapterInvocation.Peeking(source, service, cores, middle, asked),
                seen,
                from,
                began,
                cancellationToken);

            if (peeked.Fault is ChapterRunFault.TookTooLong)
            {
                asides.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"looking at the picture was stopped after {patience:c}, with {lookedThrough} of the {silences.Count} quiet stretches looked at"));

                break;
            }

            if (peeked.Succeeded)
            {
                lookedThrough++;

                continue;
            }

            refused++;
        }

        if (refused > 0)
        {
            asides.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{refused} of the looks refused, so what lies around those quiet stretches went unseen"));
        }

        WatermarkWatch? watched = asked.Watermark
            ? await WatchedAsync(source, service, learnedAhead, cores, from, began, asides, cancellationToken)
            : null;

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

        var evidence = new ChapterEvidence
        {
            Silences = silences,
            Blacks = blacks,
            Scenes = changes,
            Sightings = watched is not null && learnedAhead is not null ? watched.Sightings(timeline, artefactLength) : [],
        };
        ChapterDetection read = ChapterGrid.Mark(evidence, artefactLength, asked);

        if (asides.Count > 0)
        {
            read = read.Noting(string.Join("; ", asides));
        }

        return watched?.Learned() is { } learned ? read.Learning(learned) : read;
    }

    /// <summary>
    /// The order the quiet stretches are looked at in: the longest first, then the earliest.
    /// </summary>
    private static IEnumerable<ChapterSpan> Ranked(List<ChapterSpan> silences)
        => silences
            .OrderByDescending(quiet => quiet.Ends - quiet.Starts)
            .ThenBy(quiet => quiet.Starts);

    private static ChapterSpan? Placed(ChapterSpan reported, EncodeTimeline timeline, TimeSpan artefactLength)
        => ChapterClock.OnTheArtefact(reported, timeline.SourceStart, timeline.HeadSkip, artefactLength);

    private string? WhyNothingWasReadAtAll(ChapterRunOutcome ran, string doing)
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

    private async Task<WatermarkWatch?> WatchedAsync(
        string source,
        ServiceId service,
        WatermarkMask? learnedAhead,
        int cores,
        DateTimeOffset from,
        Func<RunningProgramme, Task> began,
        List<string> asides,
        CancellationToken cancellationToken)
    {
        TimeSpan left = patience - (clock.GetUtcNow() - from);

        if (left <= TimeSpan.Zero)
        {
            asides.Add("no time was left to watch the picture for the station's watermark, so no watermark was learned or used");

            return null;
        }

        var watch = new WatermarkWatch(learnedAhead);
        ChapterRunOutcome watching = await FfmpegChapterRun.PicturedAsync(
            machine.Programme,
            FfmpegChapterInvocation.Watching(source, service, cores),
            WatermarkFrame.Pixels,
            watch.Pictured,
            watch.Complained,
            left,
            began,
            clock,
            cancellationToken);

        if (!watching.Succeeded)
        {
            asides.Add(watching.Fault is ChapterRunFault.TookTooLong
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"watching the picture for the station's watermark was stopped after {left:c}, so no watermark was learned or used")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"watching the picture for the station's watermark ended without a reading ({watching.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? watching.Fault.ToString()}), so no watermark was learned or used"));

            return null;
        }

        if (learnedAhead is null)
        {
            asides.Add("no watermark had been learned ahead from another recording of this service, so none judged this one");
        }

        return watch;
    }

    private async Task<ChapterRunOutcome> RunAsync(
        IReadOnlyList<string> arguments,
        ChapterLog log,
        DateTimeOffset from,
        Func<RunningProgramme, Task> began,
        CancellationToken cancellationToken)
    {
        TimeSpan left = patience - (clock.GetUtcNow() - from);

        return left <= TimeSpan.Zero
            ? new ChapterRunOutcome(null, ChapterRunFault.TookTooLong, string.Empty)
            : await FfmpegChapterRun.RunAsync(
                machine.Programme,
                arguments,
                log.Said,
                log.Complained,
                left,
                began,
                clock,
                cancellationToken);
    }
}
