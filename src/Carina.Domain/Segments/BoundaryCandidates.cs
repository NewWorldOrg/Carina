namespace Carina.Domain.Segments;

/// <summary>
/// Finds where a segment may start or end in one recording, from the cues a break is cut at: a silence,
/// sound quieter than the threshold for at least <see cref="ShortestQuiet"/>, at its middle; black,
/// frames darker than the threshold for as long, at its middle; the logo coming on or going off, each
/// state held for <see cref="HeldSeconds"/> seconds; the edges of what the catalog heard as a kind it counts as,
/// since a stretch of a kind it cannot tell ends where its match stopped, which may lie inside an advert; the
/// programme's start and end by the guide; and the recording's own ends. A change of scene, a frame that moved
/// from the one before by the threshold or more, is no candidate of its own: a silence with one within
/// <see cref="CorroboratedWithin"/> of its middle, with black over it, or as quiet on average as
/// <see cref="EditedDbfs"/>, the silence an edit leaves rather than a pause, is a silent cut. Nothing past
/// the recording's length is a candidate. Going forward in time, a candidate within
/// <see cref="MergedWithin"/> of the strongest of those merged before it joins them: a recording end is
/// the strongest, then a silent cut, a catalog edge, a silence or black, and the rest.
/// </summary>
public static class BoundaryCandidates
{
    public const int HeldSeconds = 5;

    public static readonly TimeSpan ShortestQuiet = TimeSpan.FromSeconds(0.15);

    public static readonly TimeSpan CorroboratedWithin = TimeSpan.FromSeconds(0.5);

    public static readonly TimeSpan MergedWithin = TimeSpan.FromSeconds(2);

    public const double EditedDbfs = -90;

    private const double FullScale = byte.MaxValue;

    private const BoundaryCue Anchors = BoundaryCue.SilentCut | BoundaryCue.CatalogEdge | BoundaryCue.RecordingEdge;

    public static IReadOnlyList<BoundaryCandidate> Find(SegmentEvidence evidence, SegmentContext context, bool[]? sightings, SegmentThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Found(
            evidence,
            thresholds,
            [
                .. LogoChanges(sightings ?? []),
                .. context.Catalog.Where(heard => heard.Kind is not null).SelectMany(heard => new[] { new Raw(heard.Starts, BoundaryCue.CatalogEdge), new Raw(heard.Ends, BoundaryCue.CatalogEdge) }),
                .. new[] { context.ProgrammeStarts, context.ProgrammeEnds }.OfType<TimeSpan>().Select(at => new Raw(at, BoundaryCue.GuideEdge)),
            ]);
    }

    /// <summary>
    /// The candidates the recording's sound and picture alone give: its silences, black and its own ends.
    /// </summary>
    public static IReadOnlyList<BoundaryCandidate> Find(SegmentEvidence evidence, SegmentThresholds thresholds) => Found(evidence, thresholds, []);

    private static List<BoundaryCandidate> Found(SegmentEvidence evidence, SegmentThresholds thresholds, List<Raw> known)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(thresholds);

        List<Stretch> blacks = Blacks(evidence.Frames, thresholds);
        List<TimeSpan> scenes = Scenes(evidence.Frames, thresholds);
        List<Raw> raw =
        [
            .. Silences(evidence.Loudness, thresholds, blacks, scenes),
            .. blacks.Select(black => new Raw(black.Middle, BoundaryCue.Black)),
            .. known,
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

            double mean = below.Skip(from).Take(to - from).Average();
            bool cut = mean >= -EditedDbfs
                || scenes.Any(scene => (scene - quiet.Middle).Duration() <= CorroboratedWithin)
                || blacks.Any(black => black.From <= quiet.To && black.To >= quiet.From);
            double depth = mean - quieterThan;

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

    internal static List<TimeSpan> Scenes(ReadOnlySpan<PlacedFrame> frames, SegmentThresholds thresholds)
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

    private static IEnumerable<Raw> LogoChanges(bool[] seen)
        => Enumerable.Range(HeldSeconds, Math.Max(0, seen.Length - (2 * HeldSeconds) + 1))
            .Where(at => seen[at] != seen[at - 1] && Held(seen, at - HeldSeconds, at) && Held(seen, at, at + HeldSeconds))
            .Select(at => new Raw(TimeSpan.FromSeconds(at), BoundaryCue.LogoChange));

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
