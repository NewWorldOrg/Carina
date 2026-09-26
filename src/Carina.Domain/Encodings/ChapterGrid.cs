using System.Globalization;
using System.Runtime.CompilerServices;

namespace Carina.Domain.Encodings;

/// <summary>
/// Works out where the breaks in a recording are from what was observed of it.
/// </summary>
/// <remarks>
/// A pod is a pair of corroborated boundaries between one and <see cref="LongestPair"/> grid steps
/// apart, off a whole number of steps by no more than the tolerance. Overlapping pods merge, a pod
/// shorter than one grid step or longer than <see cref="LongestBreak"/> steps is dropped, a pod within
/// the tolerance of the grid is pulled onto it, and programme within <see cref="Adjacent"/> of either
/// end or of another break is given to the break beside it. The reading is thrown away whole when it
/// takes more of the length for breaks, or more marks, than allowed. What comes back covers the whole
/// length with no gap. A grid of no length, a negative tolerance or one of half the grid or wider, a
/// threshold or valve that is no share of the whole, and no marks allowed are refused.
/// <para>
/// A station watermark only takes a pod away: a pod in which the mark stayed on screen in more than
/// <see cref="WatermarkedShare"/> of the pictures looked at inside it is programme. Pictures within
/// <see cref="Adjacent"/> of a pod's edges are not counted, a pod with fewer than
/// <see cref="FewestSightingsInsideABreak"/> pictures inside it is not judged by the mark, and a mark
/// on screen in at least <see cref="WatermarkNearlyEverywhere"/> of all pictures looked at is not used.
/// </para>
/// </remarks>
public static class ChapterGrid
{
    public const int LongestPair = 12;

    public const int LongestBreak = 20;

    public const double WatermarkedShare = 0.5;

    public const double WatermarkNearlyEverywhere = 0.95;

    public const int FewestSightingsInsideABreak = 3;

    public static readonly TimeSpan Nearby = TimeSpan.FromSeconds(0.5);

    public static readonly TimeSpan Adjacent = TimeSpan.FromSeconds(2);

    public static ChapterDetection Mark(ChapterEvidence evidence, TimeSpan length, ChapterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero, nameof(length));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(settings.Grid, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.GridTolerance, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(settings.GridTolerance, settings.Grid / 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.MostChapters, 1);
        Bounded(settings.Scene);
        Bounded(settings.MostBreakShare);

        List<string> asides = [];
        List<ChapterSpan> breaks = Settled(
            Unbranded(
                Snapped(Sized(Merged(Paired(Corroborated(evidence, length, settings), settings)), settings), settings, length),
                evidence.Sightings,
                asides),
            length);

        if (breaks.Count is 0)
        {
            return Noted(ChapterDetection.NothingFound(), asides);
        }

        double share = Math.Clamp(
            breaks.Sum(gap => (gap.Ends - gap.Starts).TotalSeconds) / length.TotalSeconds,
            0,
            1);

        if (share > settings.MostBreakShare)
        {
            return Noted(
                ChapterDetection.Discarded(
                    share,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"the breaks came to {share:0.000} of the length, and no more than {settings.MostBreakShare:0.000} of it may be break")),
                asides);
        }

        IReadOnlyList<ChapterSegment> segments = Tiled(breaks, length);

        return Noted(
            segments.Count - 1 > settings.MostChapters
                ? ChapterDetection.Discarded(
                    share,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"the reading put in {segments.Count - 1} marks, and no more than {settings.MostChapters} may be put in"))
                : ChapterDetection.Marked(segments, length, share),
            asides);
    }

    private static ChapterDetection Noted(ChapterDetection read, List<string> asides)
        => asides.Count is 0 ? read : read.Noting(string.Join("; ", asides));

    private static void Bounded(double share, [CallerArgumentExpression(nameof(share))] string? named = null)
    {
        if (double.IsNaN(share) || share <= 0 || share > 1)
        {
            throw new ArgumentOutOfRangeException(
                named,
                share,
                "A share of the whole lies above none of it and at most all of it.");
        }
    }

    private static List<TimeSpan> Corroborated(ChapterEvidence evidence, TimeSpan length, ChapterSettings settings)
    {
        List<TimeSpan> boundaries = [];

        foreach (ChapterSpan quiet in evidence.Silences)
        {
            TimeSpan at = quiet.Starts + ((quiet.Ends - quiet.Starts) / 2);

            if (at < TimeSpan.Zero || at > length)
            {
                continue;
            }

            bool changed = evidence.Scenes.Any(
                scene => scene.Score >= settings.Scene && (scene.At - at).Duration() <= Nearby);
            bool darkened = evidence.Blacks.Any(
                black => black.Starts <= quiet.Ends && black.Ends >= quiet.Starts);

            if (changed || darkened)
            {
                boundaries.Add(at);
            }
        }

        boundaries.Sort();

        return boundaries;
    }

    private static List<ChapterSpan> Paired(List<TimeSpan> boundaries, ChapterSettings settings)
    {
        List<ChapterSpan> pods = [];
        TimeSpan furthest = settings.Grid * LongestPair;

        for (int first = 0; first < boundaries.Count; first++)
        {
            for (int second = first + 1; second < boundaries.Count; second++)
            {
                TimeSpan apart = boundaries[second] - boundaries[first];

                if (apart >= settings.Grid && apart <= furthest && OffTheGrid(apart, settings.Grid) <= settings.GridTolerance)
                {
                    pods.Add(new ChapterSpan(boundaries[first], boundaries[second]));
                }
            }
        }

        return pods;
    }

    private static TimeSpan OffTheGrid(TimeSpan apart, TimeSpan grid)
    {
        var over = new TimeSpan(apart.Ticks % grid.Ticks);

        return over <= grid - over ? over : grid - over;
    }

    private static List<ChapterSpan> Merged(List<ChapterSpan> pods)
    {
        pods.Sort((one, other) => one.Starts.CompareTo(other.Starts));

        List<ChapterSpan> joined = [];

        foreach (ChapterSpan pod in pods)
        {
            if (joined.Count > 0 && pod.Starts <= joined[^1].Ends)
            {
                joined[^1] = joined[^1] with { Ends = pod.Ends > joined[^1].Ends ? pod.Ends : joined[^1].Ends };
                continue;
            }

            joined.Add(pod);
        }

        return joined;
    }

    private static List<ChapterSpan> Sized(List<ChapterSpan> pods, ChapterSettings settings)
        => [.. pods.Where(pod => pod.Ends - pod.Starts >= settings.Grid
            && pod.Ends - pod.Starts <= settings.Grid * LongestBreak)];

    private static List<ChapterSpan> Snapped(List<ChapterSpan> pods, ChapterSettings settings, TimeSpan length)
    {
        List<ChapterSpan> laid = [];

        foreach (ChapterSpan pod in pods)
        {
            TimeSpan lasting = pod.Ends - pod.Starts;
            double steps = Math.Round(lasting.Ticks / (double)settings.Grid.Ticks, MidpointRounding.AwayFromZero);
            TimeSpan moved = (settings.Grid * steps) - lasting;
            bool pulls = steps >= 1
                && moved.Duration() <= settings.GridTolerance
                && pod.Ends + moved > pod.Starts
                && pod.Ends + moved <= length;

            laid.Add(pulls ? pod with { Ends = pod.Ends + moved } : pod);
        }

        return laid;
    }

    private static List<ChapterSpan> Unbranded(
        List<ChapterSpan> pods,
        IReadOnlyList<WatermarkSighting> sightings,
        List<string> asides)
    {
        if (sightings.Count is 0 || pods.Count is 0)
        {
            return pods;
        }

        double everywhere = sightings.Count(sighting => sighting.Seen) / (double)sightings.Count;

        if (everywhere >= WatermarkNearlyEverywhere)
        {
            asides.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"the watermark learned ahead was on screen in {everywhere:0.000} of the pictures looked at, so it told programme from break nowhere and was not used"));

            return pods;
        }

        List<ChapterSpan> kept = [];
        int branded = 0;

        foreach (ChapterSpan pod in pods)
        {
            WatermarkSighting[] inside =
            [
                .. sightings.Where(sighting => sighting.At > pod.Starts + Adjacent && sighting.At < pod.Ends - Adjacent),
            ];

            if (inside.Length >= FewestSightingsInsideABreak
                && inside.Count(sighting => sighting.Seen) > inside.Length * WatermarkedShare)
            {
                branded++;

                continue;
            }

            kept.Add(pod);
        }

        if (branded > 0)
        {
            asides.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{branded} of the {pods.Count} candidate breaks were taken away because the watermark learned ahead stayed on screen through them"));
        }

        return kept;
    }

    private static List<ChapterSpan> Settled(List<ChapterSpan> pods, TimeSpan length)
    {
        List<ChapterSpan> settled = [];

        foreach (ChapterSpan pod in pods)
        {
            ChapterSpan gap = pod;

            if (gap.Starts <= Adjacent)
            {
                gap = gap with { Starts = TimeSpan.Zero };
            }

            if (length - gap.Ends <= Adjacent)
            {
                gap = gap with { Ends = length };
            }

            if (settled.Count > 0 && gap.Starts - settled[^1].Ends <= Adjacent)
            {
                settled[^1] = settled[^1] with { Ends = gap.Ends };
                continue;
            }

            settled.Add(gap);
        }

        return settled;
    }

    private static IReadOnlyList<ChapterSegment> Tiled(List<ChapterSpan> breaks, TimeSpan length)
    {
        List<ChapterSegment> laid = [];
        TimeSpan reached = TimeSpan.Zero;

        foreach (ChapterSpan gap in breaks)
        {
            if (gap.Starts > reached)
            {
                laid.Add(new ChapterSegment(reached, gap.Starts, ChapterKind.Programme));
            }

            laid.Add(new ChapterSegment(gap.Starts, gap.Ends, ChapterKind.Break));
            reached = gap.Ends;
        }

        if (reached < length)
        {
            laid.Add(new ChapterSegment(reached, length, ChapterKind.Programme));
        }

        return laid;
    }
}
