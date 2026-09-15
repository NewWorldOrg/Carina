using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Looks for the breaks in one source with the ffmpeg this image already carries, in three passes.
/// The first listens to the whole of the sound and writes down every stretch that went quiet,
/// decoding no picture at all; the second watches the whole of the picture for the station's
/// watermark, decoding only the pictures that stand on their own and one a second of those; the
/// third looks at six seconds of picture around each quiet stretch and writes down where it went
/// black and by how much it changed. That is what makes this affordable: the whole length is heard,
/// the watermark is watched at one picture a second, and only a few seconds either side of a
/// candidate are seen whole. What the passes observed is handed to <see cref="ChapterGrid"/>, which
/// decides — nothing is decided here.
/// <para>
/// The watermark is learned from this source and handed back beside the reading, for the recordings
/// of the same service read after it; this source is judged only by a watermark learned ahead of it,
/// from another recording, and with none when there is none (BR-ED2-007). A machine told not to
/// watch for the watermark runs no second pass at all.
/// </para>
/// <para>
/// This answers, it does not fail. A programme that is not on this machine, one that refused while
/// listening, one that outlived <see cref="Patience"/> before a word of the sound was read, and a
/// source measured against some other clock all come back as a reading that could not be made, so
/// an encode that would have run without anyone looking is not failed by the looking. What is
/// written down beside such a reading is built here out of the exit code and the reason: not one
/// word of what the programme said is kept, because what it says carries the source it was
/// reading.
/// </para>
/// <para>
/// Only the sound is read whole; watching for the watermark and looking at the picture are what can
/// fall short. A watch that refused or ran out of time leaves the reading made without a watermark
/// and learns nothing. Looking at the picture has a ceiling on it, at
/// <see cref="MostLooksPerMark"/> looks for every mark the reading is allowed, taking the longest
/// quiet stretches first. Nothing that happens to one of those looks throws the reading away: one
/// that refused leaves its own stretch uncorroborated, and running out of time stops the looking
/// where it stands. Either way what has been gathered by then is still handed to
/// <see cref="ChapterGrid"/> — a reading of part of the evidence is the reading that part gives —
/// and the answer says on its face that it was made that way.
/// </para>
/// <para>
/// How much of the machine the passes may take is handed in rather than read here, so that the
/// looking and the encode that follows it are bounded by the one cap the operator holds
/// (BR-ED2-005).
/// </para>
/// <para>
/// Every programme any pass starts is handed to the caller before it is read from, on the same
/// terms the encode's own run is written down on, so that a process killed mid-look does not leave
/// an ffmpeg nobody has a record of (BR-ED2-011).
/// </para>
/// </summary>
public sealed class FfmpegChapterDetector(
    MachineSettings machine,
    EncodeSettings settings,
    TimeProvider clock,
    TimeSpan patience) : IChapterDetector
{
    /// <summary>
    /// How many quiet stretches are worth looking at the picture around, as a multiple of the
    /// marks a reading is allowed to put in. Two corroborated boundaries make one pod, so a
    /// reading allowed so many marks can use about twice that many boundaries and the rest of this
    /// is slack for the ones that corroborate nothing. Without a ceiling the number of looks is
    /// whatever the sound happened to do: a two-hour recording of people talking reports hundreds
    /// of quiet stretches, and starting one programme after another for all of them spends the
    /// whole of <see cref="Patience"/> on a machine that is recording and then comes back having
    /// read nothing at all.
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
        WatermarkWatch? watched = asked.Watermark
            ? await WatchedAsync(source, service, learnedAhead, cores, from, began, asides, cancellationToken)
            : null;

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
    /// The order the quiet stretches are worth looking at the picture around in: the longest
    /// first, because a stretch of silence long enough to be an advertisement break is the one
    /// most likely to be one, and then the earliest, so that the order is the same twice over the
    /// same reading.
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
        var watch = new WatermarkWatch(learnedAhead);
        TimeSpan left = patience - (clock.GetUtcNow() - from);
        ChapterRunOutcome watching = left <= TimeSpan.Zero
            ? new ChapterRunOutcome(null, ChapterRunFault.TookTooLong, string.Empty)
            : await FfmpegChapterRun.PicturedAsync(
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
                    $"watching the picture for the station's watermark was stopped after {patience:c}, so no watermark was learned or used")
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
