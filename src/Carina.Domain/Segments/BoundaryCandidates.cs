namespace Carina.Domain.Segments;

/// <summary>
/// Finds where a segment may start or end in one recording: a silence, sound quieter than the
/// threshold for at least <see cref="ShortestQuiet"/>, at its middle; black, frames darker than the
/// threshold for as long, at its middle; a change of scene, a frame that moved from the one before by the
/// threshold or more; a change in how alike the channels are, the greatest change of their mean over
/// <see cref="ChannelWindow"/> on either side, of at least <see cref="ChannelChange"/>; the logo and the
/// captions coming on or going off, each state held for <see cref="HeldSeconds"/> seconds; the edges of
/// what the catalog heard; the programme's start and end by the guide; and the recording's own ends. A
/// silence with a change of scene within <see cref="CorroboratedWithin"/> of its middle, or black over
/// it, is a silent cut. Nothing past the recording's length is a candidate. Going forward in time, a
/// candidate within <see cref="MergedWithin"/> of the strongest of those merged before it joins them: a
/// recording end is the strongest, then a silent cut, a catalog edge, a silence or black, and the rest.
/// </summary>
public static class BoundaryCandidates
{
    public const double ChannelChange = 0.5;

    public const int HeldSeconds = 2;

    public static readonly TimeSpan ShortestQuiet = TimeSpan.FromSeconds(0.15);

    public static readonly TimeSpan CorroboratedWithin = TimeSpan.FromSeconds(0.5);

    public static readonly TimeSpan MergedWithin = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan ChannelWindow = TimeSpan.FromSeconds(1);

    private const double FullScale = byte.MaxValue;

    private const BoundaryCue Anchors = BoundaryCue.SilentCut | BoundaryCue.CatalogEdge | BoundaryCue.RecordingEdge;

    public static IReadOnlyList<BoundaryCandidate> Find(SegmentEvidence evidence, SegmentContext context, bool[]? sightings, SegmentThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(thresholds);

        List<Stretch> blacks = Blacks(evidence.Frames, thresholds);
        List<TimeSpan> scenes = Scenes(evidence.Frames, thresholds);
        List<Raw> raw =
        [
            .. Silences(evidence.Loudness, thresholds, blacks, scenes),
            .. blacks.Select(black => new Raw(black.Middle, BoundaryCue.Black)),
            .. scenes.Select(at => new Raw(at, BoundaryCue.SceneChange)),
            .. ChannelChanges(evidence.Channels),
            .. Flips(sightings ?? [], TimeSpan.FromSeconds(1), BoundaryCue.LogoChange),
            .. Flips(Shown(evidence.Captions), TimeSpan.FromSeconds(1), BoundaryCue.CaptionChange),
            .. context.Catalog.SelectMany(heard => new[] { new Raw(heard.Starts, BoundaryCue.CatalogEdge), new Raw(heard.Ends, BoundaryCue.CatalogEdge) }),
            .. new[] { context.ProgrammeStarts, context.ProgrammeEnds }.OfType<TimeSpan>().Select(at => new Raw(at, BoundaryCue.GuideEdge)),
            new Raw(TimeSpan.Zero, BoundaryCue.RecordingEdge),
            new Raw(evidence.Length, BoundaryCue.RecordingEdge),
        ];

        return Merged([.. raw.Where(candidate => candidate.At >= TimeSpan.Zero && candidate.At <= evidence.Length)]);
    }

    /// <summary>
    /// The seconds of the candidates, other than the recording's own ends.
    /// </summary>
    public static IReadOnlyList<TimeSpan> Snaps(IReadOnlyList<BoundaryCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return [.. candidates.Where(candidate => !candidate.IsRecordingEdge).Select(candidate => candidate.At)];
    }

    private static List<Raw> Silences(ReadOnlySpan<byte> loudness, SegmentThresholds thresholds, List<Stretch> blacks, List<TimeSpan> scenes)
    {
        double quieterThan = -thresholds.SilenceDbfs;
        double[] below = new double[loudness.Length];

        for (int at = 0; at < loudness.Length; at++)
        {
            below[at] = SoundLoudness.DecibelsBelowFullScale(loudness[at]);
        }

        List<Raw> found = [];

        foreach ((int from, int to) in Runs([.. below.Select(decibels => decibels > quieterThan)]))
        {
            Stretch quiet = new(SegmentEvidence.LoudnessStep * from, SegmentEvidence.LoudnessStep * to);

            if (quiet.Length < ShortestQuiet)
            {
                continue;
            }

            bool cut = scenes.Any(scene => (scene - quiet.Middle).Duration() <= CorroboratedWithin)
                || blacks.Any(black => black.From <= quiet.To && black.To >= quiet.From);
            double depth = below.Skip(from).Take(to - from).Average() - quieterThan;

            found.Add(new Raw(quiet.Middle, cut ? BoundaryCue.Silence | BoundaryCue.SilentCut : BoundaryCue.Silence, depth, quiet.Length));
        }

        return found;
    }

    private static List<Stretch> Blacks(ReadOnlySpan<PlacedFrame> frames, SegmentThresholds thresholds)
    {
        bool[] dark = new bool[frames.Length];
        TimeSpan[] times = new TimeSpan[frames.Length + 1];

        for (int at = 0; at < frames.Length; at++)
        {
            dark[at] = frames[at].Light.Brightness / FullScale < thresholds.BlackLevel;
            times[at] = frames[at].At;
        }

        if (frames.Length > 0)
        {
            times[^1] = frames.Length > 1 ? (frames[^1].At * 2) - frames[^2].At : frames[^1].At;
        }

        return [.. Runs(dark).Select(run => new Stretch(times[run.From], times[run.To])).Where(black => black.Length >= ShortestQuiet)];
    }

    private static List<TimeSpan> Scenes(ReadOnlySpan<PlacedFrame> frames, SegmentThresholds thresholds)
    {
        List<TimeSpan> found = [];

        foreach (PlacedFrame frame in frames)
        {
            if (frame.Light.Change / FullScale >= thresholds.SceneChange)
            {
                found.Add(frame.At);
            }
        }

        return found;
    }

    private static List<Raw> ChannelChanges(ReadOnlySpan<ChannelDifference> channels)
    {
        int window = (int)(ChannelWindow.Ticks / SegmentEvidence.ChannelStep.Ticks);
        double[] sums = new double[channels.Length + 1];
        int[] counts = new int[channels.Length + 1];

        for (int at = 0; at < channels.Length; at++)
        {
            double? likeness = channels[at].Likeness;

            sums[at + 1] = sums[at] + (likeness ?? 0);
            counts[at + 1] = counts[at] + (likeness is null ? 0 : 1);
        }

        double[] changes = new double[channels.Length + 1];

        for (int at = 0; at <= channels.Length; at++)
        {
            changes[at] = at < window || at + window > channels.Length
                ? double.NaN
                : Math.Abs(Mean(sums, counts, at, at + window, window) - Mean(sums, counts, at - window, at, window));
        }

        return [.. Enumerable.Range(0, changes.Length)
            .Where(at => changes[at] >= ChannelChange && IsTheGreatestAround(changes, at, window))
            .Select(at => new Raw(SegmentEvidence.ChannelStep * at, BoundaryCue.ChannelChange))];
    }

    private static double Mean(double[] sums, int[] counts, int from, int to, int window)
    {
        int measured = counts[to] - counts[from];

        return measured * 2 >= window ? (sums[to] - sums[from]) / measured : double.NaN;
    }

    private static bool IsTheGreatestAround(double[] changes, int at, int window)
    {
        int from = Math.Max(0, at - window);
        int to = Math.Min(changes.Length - 1, at + window);

        return Enumerable.Range(from, at - from).All(before => !(changes[before] >= changes[at]))
            && Enumerable.Range(at + 1, to - at).All(after => !(changes[after] > changes[at]));
    }

    private static bool[] Shown(ReadOnlySpan<byte> captions)
    {
        bool[] shown = new bool[captions.Length];

        for (int second = 0; second < captions.Length; second++)
        {
            shown[second] = captions[second] is CaptionPresence.Shown;
        }

        return shown;
    }

    private static IEnumerable<Raw> Flips(bool[] states, TimeSpan step, BoundaryCue cue)
        => Enumerable.Range(HeldSeconds, Math.Max(0, states.Length - (2 * HeldSeconds) + 1))
            .Where(at => states[at] != states[at - 1] && Held(states, at - HeldSeconds, at) && Held(states, at, at + HeldSeconds))
            .Select(at => new Raw(step * at, cue));

    private static bool Held(bool[] states, int from, int to) => states.AsSpan(from, to - from).IndexOfAnyExcept(states[from]) < 0;

    private static IEnumerable<(int From, int To)> Runs(bool[] holds)
    {
        int start = -1;

        for (int at = 0; at <= holds.Length; at++)
        {
            bool held = at < holds.Length && holds[at];

            if (held && start < 0)
            {
                start = at;
            }
            else if (!held && start >= 0)
            {
                yield return (start, at);
                start = -1;
            }
        }
    }

    private static List<BoundaryCandidate> Merged(List<Raw> raw)
    {
        List<List<Raw>> merged = [];

        foreach (Raw candidate in raw.OrderBy(candidate => candidate.At).ThenByDescending(candidate => candidate.Strength).ThenBy(candidate => candidate.Cue))
        {
            if (merged.Count > 0 && candidate.At - Strongest(merged[^1]).At <= MergedWithin)
            {
                merged[^1].Add(candidate);
                continue;
            }

            merged.Add([candidate]);
        }

        return [.. merged.Select(Candidate)];
    }

    private static Raw Strongest(List<Raw> merged) => merged.MaxBy(candidate => candidate.Strength);

    private static BoundaryCandidate Candidate(List<Raw> merged)
    {
        Raw strongest = Strongest(merged);
        TimeSpan[] anchored = [.. merged.Where(candidate => (candidate.Cue & Anchors) is not BoundaryCue.None).Select(candidate => candidate.At)];
        Raw longest = merged.MaxBy(candidate => candidate.Lasting);

        return new BoundaryCandidate(
            strongest.At,
            anchored.Length is 0 ? strongest.At : anchored.Min(),
            anchored.Length is 0 ? strongest.At : anchored.Max(),
            merged.Aggregate(BoundaryCue.None, (cues, candidate) => cues | candidate.Cue),
            longest.Depth,
            longest.Lasting);
    }

    private readonly record struct Stretch(TimeSpan From, TimeSpan To)
    {
        public TimeSpan Length => To - From;

        public TimeSpan Middle => From + ((To - From) / 2);
    }

    private readonly record struct Raw(TimeSpan At, BoundaryCue Cue, double Depth = 0, TimeSpan Lasting = default)
    {
        public int Strength => Cue switch
        {
            BoundaryCue.RecordingEdge => 5,
            _ when Cue.HasFlag(BoundaryCue.SilentCut) => 4,
            BoundaryCue.CatalogEdge => 3,
            BoundaryCue.Silence or BoundaryCue.Black => 2,
            _ => 1,
        };
    }
}
