using System.Globalization;
using System.Runtime.CompilerServices;

namespace Carina.Domain.Encodings;

/// <summary>
/// Where the breaks in a recording are, worked out from what was observed of it and from nothing
/// else. A pod of advertisements is laid out in whole multiples of the grid, so no boundary is
/// ever taken on its own: only a pair of corroborated boundaries a whole number of grid steps
/// apart makes a break, which is what leaves a programme carrying no advertisements marked
/// nowhere. Pods that overlap are one pod, a pod is pulled onto the grid if it sits within the
/// tolerance of it, and a sliver of programme too short to be worth a mark is given to the break
/// beside it. Two safety valves follow, and both throw the reading away whole rather than leave
/// half of it believed: one for a reading that took more of the length for breaks than it is
/// allowed to, one for a reading that put in more marks than it is allowed to. What comes back
/// covers the whole length end to end with no gap. What it was told to read by is checked before
/// anything is read, so a grid of no length, a tolerance half that grid or wider, a threshold or a
/// valve that is no share of the whole, and a reading allowed no marks at all are all refused here
/// rather than worked around further in.
/// <para>
/// A station's watermark is only ever used to take a pod away, never to make one: a pod through
/// which the mark learned ahead stayed on screen in more than <see cref="WatermarkedShare"/> of the
/// pictures looked at inside it is programme, not a break. The pictures at either edge
/// of a pod are left out, because the mark comes and goes around the boundary itself, and a pod with
/// fewer than <see cref="FewestSightingsInsideABreak"/> pictures inside it is not taken away on their
/// word. A mark that was on screen in <see cref="WatermarkNearlyEverywhere"/> of all the pictures
/// looked at tells programme from break nowhere — it is the picture of a programme without
/// advertisements, or something that is not a watermark at all — and is not used.
/// </para>
/// </summary>
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
