namespace Carina.Domain.Segments;

/// <summary>
/// A segment, the stretch between two neighbouring boundary candidates: where it starts and ends, its
/// length fitted to the grid from any of the cuts its edges took in, its features, and the kind the catalog
/// fixes it to with how likely that is, when it does (<see cref="SegmentSequence"/>).
/// </summary>
public sealed record SegmentPiece(TimeSpan Starts, TimeSpan Ends, TimeSpan Fitted, SegmentFeatureValues Features, CatalogFix? Fixed = null)
{
    public TimeSpan Length => Ends - Starts;

    /// <summary>
    /// Whether the segment starts at a seam (<see cref="SegmentFeatures.Seam"/>), which no opening or ending runs across.
    /// </summary>
    public bool AfterASeam { get; init; }
}

/// <summary>
/// Computes the features of each segment between neighbouring boundary candidates: its length, how far
/// it lies off the grid and whether it is a multiple of it, and one of at most <see cref="LongestAdvertSteps"/>
/// steps, as long as an advert lasts, since a pod of adverts is cut at every advert and a longer stretch without a cut
/// inside is seldom one; the depth and length of the silence at each
/// edge, and whether both edges are silent cuts; the share of the seconds inside it, away from its edges
/// by <see cref="LogoAwayFromTheEdges"/>, that show the logo, when at least
/// <see cref="FewestLogoSeconds"/> do; where it lies from
/// the programme's start and end, as shares of the programme's length, and the share of it outside the
/// guide; the share of earlier episodes with a break within <see cref="PastWithin"/> of the same place from their
/// programme's start, with an opening within <see cref="PastThemeWithin"/> of it, and with an ending within that of the
/// same place before their programme's end, an opening or an ending saying where it lies and no more; the share of its seconds with captions; the channels' mean likeness,
/// and how far it changes across each edge over <see cref="ChannelWindow"/> on either side, when at
/// least half of each window was measured; the changes of scene per ten seconds; the programme's first
/// genre; a multiple of the grid without the logo; whether it starts where an opening does, from
/// <see cref="CatalogKinds.EdgeWithin"/> before the programme's start to <see cref="CatalogKinds.ThemeEdgeWithin"/>
/// after it, and ends where an ending does, as far from the programme's end the other way; whether it lasts as
/// long as a whole opening or ending commonly does, from <see cref="WholeThemeShortest"/> to
/// <see cref="WholeThemeLongest"/>; and its loudness range, the decibels between the 10th and the 90th percentile
/// of its momentary loudness, the mean power over <see cref="MomentaryWindow"/> every <see cref="MomentaryHop"/>,
/// at least <see cref="LoudnessAwayFromTheEdges"/> and half the silence at each edge away from its edges, or over
/// <see cref="ShortestLoudnessRange"/> around its middle when less than that lies there, since a few seconds say little:
/// a song keeps its loudness, speech keeps stopping. These say
/// what an opening or an ending is like in any series, its first episode included. Whether an advert lies beside it
/// (<see cref="SegmentFeature.AdvertBeside"/>) says whether it is one of a pod: it runs from one cut between adverts to
/// the next, as long as an advert, and so does the stretch to the cut before it or after it. A feature without the readings
/// to compute it is not known. The programme spans the recording when the guide says nothing. The catalog is no feature:
/// it fixes a segment that its stretches of one kind cover more than half of to that kind, a fixed part of a
/// programme to the programme, at the confidence of the surest entry of that kind over it; of two kinds that
/// do, the surer, then the earlier kind. A stretch whose kind or confidence is not known fixes nothing.
/// </summary>
public static class SegmentFeatures
{
    public const int FewestLogoSeconds = 3;

    public static readonly TimeSpan LogoAwayFromTheEdges = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan PastWithin = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan PastThemeWithin = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan ChannelWindow = TimeSpan.FromSeconds(1);

    public const int LongestAdvertSteps = 4;

    public static readonly TimeSpan WholeThemeShortest = TimeSpan.FromSeconds(55);

    public static readonly TimeSpan WholeThemeLongest = TimeSpan.FromSeconds(125);

    public static readonly TimeSpan MomentaryWindow = TimeSpan.FromMilliseconds(400);

    public static readonly TimeSpan MomentaryHop = TimeSpan.FromMilliseconds(100);

    public static readonly TimeSpan LoudnessAwayFromTheEdges = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan ShortestLoudnessRange = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan EditedFor = TimeSpan.FromSeconds(0.5);

    public static readonly TimeSpan SeamFor = TimeSpan.FromSeconds(0.8);

    private static readonly TimeSpan SceneSpan = TimeSpan.FromSeconds(10);

    public static IReadOnlyList<SegmentPiece> Of(
        IReadOnlyList<BoundaryCandidate> candidates,
        SegmentEvidence evidence,
        SegmentContext context,
        bool[]? sightings,
        SegmentThresholds thresholds)
        => Pieces(candidates, Reading.Of(candidates, evidence, context, sightings, thresholds));

    /// <summary>
    /// The segments between neighbouring candidates, and the neighbouring segments read as one (<see cref="JoinedPiece"/>)
    /// where an advert, an opening or an ending may lie across them, none of them a segment the catalog fixes, since what
    /// the catalog hears is decided by its own edges. Segments are joined as an advert, to be taken for a break only, when
    /// the whole runs from one cut between adverts (<see cref="EditedFor"/>) to the next and an advert lies beside it
    /// (<see cref="SegmentFeature.AdvertBeside"/>): inside a pod, what cuts an advert in two, such as the logo coming and
    /// going or a pause, says nothing against it. They are joined as an opening, or an ending, to be taken for that only,
    /// when the whole lasts from <see cref="CatalogKinds.ThemeShortest"/> to <see cref="CatalogKinds.ThemeLongest"/>, starts
    /// where an opening does, or ends where an ending does, as <see cref="SegmentFeature.OpeningWindow"/> and
    /// <see cref="SegmentFeature.EndingWindow"/> say, runs across no seam (<see cref="SeamFor"/>), and both its ends carry
    /// more than the logo coming or going: a song starts and stops at a silence or a cut, while the logo comes and goes
    /// with what is drawn.
    /// </summary>
    public static (IReadOnlyList<SegmentPiece> Pieces, IReadOnlyList<JoinedPiece> Joined) Read(
        IReadOnlyList<BoundaryCandidate> candidates,
        SegmentEvidence evidence,
        SegmentContext context,
        bool[]? sightings,
        SegmentThresholds thresholds)
    {
        Reading reading = Reading.Of(candidates, evidence, context, sightings, thresholds);
        IReadOnlyList<SegmentPiece> pieces = Pieces(candidates, reading);
        List<JoinedPiece> joined = [];

        for (int first = 0; first < pieces.Count; first++)
        {
            bool seam = false;

            for (int last = first + 1; last < pieces.Count && pieces[first].Fixed is null && pieces[last].Fixed is null; last++)
            {
                BoundaryCandidate head = candidates[first];
                BoundaryCandidate tail = candidates[last + 1];

                if (tail.At - head.At > CatalogKinds.ThemeLongest)
                {
                    break;
                }

                seam |= pieces[last].AfterASeam;

                if (reading.JoinedAs(head, tail, seam) is { Length: > 0 } kinds)
                {
                    joined.Add(new JoinedPiece(first, last, reading.Piece(head, tail) with { Fixed = null, AfterASeam = pieces[first].AfterASeam }, kinds));
                }
            }
        }

        return (pieces, joined);
    }

    private static SegmentPiece[] Pieces(IReadOnlyList<BoundaryCandidate> candidates, Reading reading)
        => [.. candidates.Zip(candidates.Skip(1)).Select(pair => reading.Piece(pair.First, pair.Second) with { AfterASeam = reading.Seam(pair.First) })];

    private sealed class Reading(SegmentEvidence evidence, SegmentContext context, bool[]? sightings, SegmentThresholds thresholds, IReadOnlyList<BoundaryCandidate> candidates)
    {
        private readonly BoundaryCandidate[] cuts = [.. candidates.Where(candidate => Edited(candidate, thresholds))];

        private readonly TimeSpan programmeStarts = context.ProgrammeStarts ?? TimeSpan.Zero;

        private readonly TimeSpan programmeEnds = context.ProgrammeEnds ?? evidence.Length;

        private readonly bool[]? captions = Shown(evidence);

        private readonly double?[] likeness = Likeness(evidence);

        private readonly List<TimeSpan> scenes = BoundaryCandidates.Scenes(evidence.Frames, thresholds);

        private readonly int? genre = context.Genres.Count is 0 ? null : context.Genres[0].Kind;

        private readonly double[] power = Powers(evidence);

        public static Reading Of(
            IReadOnlyList<BoundaryCandidate> candidates,
            SegmentEvidence evidence,
            SegmentContext context,
            bool[]? sightings,
            SegmentThresholds thresholds)
        {
            ArgumentNullException.ThrowIfNull(candidates);
            ArgumentNullException.ThrowIfNull(evidence);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(thresholds);

            if (candidates.Count < 2)
            {
                throw new ArgumentException("Segments lie between at least two candidates, the recording's ends.", nameof(candidates));
            }

            return new Reading(evidence, context, sightings, thresholds, candidates);
        }

        /// <summary>
        /// The kinds the segments from <paramref name="head"/> to <paramref name="tail"/> may be taken for when read
        /// as one, a seam lying between them or not: none, a break, or an opening, an ending or both.
        /// </summary>
        public SegmentKind[] JoinedAs(BoundaryCandidate head, BoundaryCandidate tail, bool seamInside)
        {
            TimeSpan length = tail.At - head.At;

            if (AdvertBeside(head, tail) is 1)
            {
                return [SegmentKind.Break];
            }

            if (seamInside || length < CatalogKinds.ThemeShortest || length > CatalogKinds.ThemeLongest || !Audible(head) || !Audible(tail))
            {
                return [];
            }

            return
            [
                .. Between(head.At, programmeStarts - CatalogKinds.EdgeWithin, programmeStarts + CatalogKinds.ThemeEdgeWithin) is 1 ? [SegmentKind.Opening] : Array.Empty<SegmentKind>(),
                .. Between(tail.At, programmeEnds - CatalogKinds.ThemeEdgeWithin, programmeEnds + CatalogKinds.EdgeWithin) is 1 ? [SegmentKind.Ending] : Array.Empty<SegmentKind>(),
            ];
        }

        /// <summary>
        /// 1 when the segment from <paramref name="head"/> to <paramref name="tail"/> runs from one cut between adverts
        /// to the next, as long as an advert, and the stretch from the cut before it to its start, or from its end to the
        /// cut after it, is as long as an advert too; 0 otherwise. A pod of adverts is a run of them.
        /// </summary>
        private double AdvertBeside(BoundaryCandidate head, BoundaryCandidate tail)
        {
            int from = Array.IndexOf(cuts, head);
            int to = Array.IndexOf(cuts, tail);

            if (from < 0 || to != from + 1 || !AnAdvert(head, tail))
            {
                return 0;
            }

            return (from > 0 && AnAdvert(cuts[from - 1], head)) || (to + 1 < cuts.Length && AnAdvert(tail, cuts[to + 1])) ? 1 : 0;
        }

        private bool AnAdvert(BoundaryCandidate head, BoundaryCandidate tail)
        {
            TimeSpan from = tail.Earliest - head.Latest > TimeSpan.Zero ? tail.Earliest - head.Latest : TimeSpan.Zero;
            TimeSpan longest = tail.Latest - head.Earliest;

            return SegmentGrid.IsAMultiple(from, longest, TimeSpan.FromSeconds(thresholds.GridToleranceSec))
                && Math.Round(SegmentGrid.Nearest(from, longest) / SegmentGrid.Step, MidpointRounding.AwayFromZero) <= LongestAdvertSteps;
        }

        /// <summary>
        /// Whether <paramref name="candidate"/> is a seam, where a broadcast is joined between the programme and an
        /// advert or between two adverts: a cut between adverts whose longest silence lasts <see cref="SeamFor"/> at
        /// least, longer than a song stops for, or that starts or ends a stretch as long as an advert to the cut before
        /// it or after it.
        /// </summary>
        public bool Seam(BoundaryCandidate candidate)
        {
            int at = Array.IndexOf(cuts, candidate);

            return at >= 0
                && (candidate.SilenceLength >= SeamFor || (at > 0 && AnAdvert(cuts[at - 1], candidate)) || (at + 1 < cuts.Length && AnAdvert(candidate, cuts[at + 1])));
        }

        /// <summary>
        /// Whether <paramref name="candidate"/> is a cut between adverts: a silent cut whose longest silence lasts
        /// <see cref="EditedFor"/> at least and is as quiet on average as <see cref="BoundaryCandidates.EditedDbfs"/>.
        /// </summary>
        private static bool Edited(BoundaryCandidate candidate, SegmentThresholds thresholds)
            => candidate.IsSilentCut && candidate.SilenceLength >= EditedFor && candidate.SilenceDepth >= thresholds.SilenceDbfs - BoundaryCandidates.EditedDbfs;

        public SegmentPiece Piece(BoundaryCandidate head, BoundaryCandidate tail)
        {
            TimeSpan shortest = tail.Earliest - head.Latest;
            TimeSpan longest = tail.Latest - head.Earliest;
            TimeSpan from = shortest > TimeSpan.Zero ? shortest : TimeSpan.Zero;
            TimeSpan fitted = SegmentGrid.Nearest(from, longest);
            double multiple = SegmentGrid.IsAMultiple(from, longest, TimeSpan.FromSeconds(thresholds.GridToleranceSec)) ? 1 : 0;
            Stretch piece = new(head.At, tail.At);
            double? logo = LogoShare(piece);
            Dictionary<string, double?> features = new(StringComparer.Ordinal)
            {
                [SegmentFeature.Length] = piece.Length.TotalSeconds,
                [SegmentFeature.GridOffset] = SegmentGrid.Off(fitted).TotalSeconds,
                [SegmentFeature.IsMultipleOf15s] = multiple,
                [SegmentFeature.HeadSilenceDepth] = head.SilenceDepth,
                [SegmentFeature.HeadSilenceLength] = head.SilenceLength.TotalSeconds,
                [SegmentFeature.TailSilenceDepth] = tail.SilenceDepth,
                [SegmentFeature.TailSilenceLength] = tail.SilenceLength.TotalSeconds,
                [SegmentFeature.EdgeSilence] = head.IsSilentCut && tail.IsSilentCut ? 1 : 0,
                [SegmentFeature.LogoShare] = logo,
                [SegmentFeature.FromStart] = (piece.From - programmeStarts) / ProgrammeLength,
                [SegmentFeature.FromEnd] = (programmeEnds - piece.To) / ProgrammeLength,
                [SegmentFeature.OutsideGuide] = OutsideGuide(piece),
                [SegmentFeature.PastCmShare] = PastShare((episode, place) => episode.HadABreakNear(place, PastWithin), piece.Middle - programmeStarts),
                [SegmentFeature.PastOpeningShare] = PastShare((episode, place) => episode.HadAnOpeningNear(place, PastThemeWithin), piece.Middle - programmeStarts),
                [SegmentFeature.PastEndingShare] = PastShare((episode, place) => episode.HadAnEndingNear(place, PastThemeWithin), programmeEnds - piece.Middle),
                [SegmentFeature.CaptionShare] = CaptionShare(piece),
                [SegmentFeature.ChannelCorrelation] = MeanLikeness(piece.From, piece.To),
                [SegmentFeature.HeadChannelChange] = ChannelChangeAcross(head.At),
                [SegmentFeature.TailChannelChange] = ChannelChangeAcross(tail.At),
                [SegmentFeature.SceneChangesPer10s] = scenes.Count(at => at >= piece.From && at < piece.To) * (SceneSpan / piece.Length),
                [SegmentFeature.MultipleOf15sWithoutLogo] = multiple * (1 - logo),
                [SegmentFeature.OpeningWindow] = Between(piece.From, programmeStarts - CatalogKinds.EdgeWithin, programmeStarts + CatalogKinds.ThemeEdgeWithin),
                [SegmentFeature.EndingWindow] = Between(piece.To, programmeEnds - CatalogKinds.ThemeEdgeWithin, programmeEnds + CatalogKinds.EdgeWithin),
                [SegmentFeature.IsThemeLength] = Between(piece.Length, WholeThemeShortest, WholeThemeLongest),
                [SegmentFeature.LoudnessRange] = LoudnessRange(piece, head, tail),
                [SegmentFeature.IsShortMultipleOf15s] = multiple * (Math.Round(fitted / SegmentGrid.Step, MidpointRounding.AwayFromZero) <= LongestAdvertSteps ? 1 : 0),
                [SegmentFeature.AdvertBeside] = AdvertBeside(head, tail),
            };

            for (int kind = 0; kind < SegmentFeature.GenreKinds; kind++)
            {
                features[SegmentFeature.Genre(kind)] = genre is null ? null : (genre == kind ? 1 : 0);
            }

            return new SegmentPiece(piece.From, piece.To, fitted, SegmentFeatureValues.Of(features), FixedBy(context.Catalog, piece));
        }

        private TimeSpan ProgrammeLength => programmeEnds > programmeStarts ? programmeEnds - programmeStarts : evidence.Length;

        private static bool[]? Shown(SegmentEvidence evidence)
        {
            if (!evidence.Captioned)
            {
                return null;
            }

            ReadOnlySpan<byte> seconds = evidence.Captions;
            bool[] shown = new bool[seconds.Length];

            for (int second = 0; second < seconds.Length; second++)
            {
                shown[second] = seconds[second] is CaptionPresence.Shown;
            }

            return shown;
        }

        private static double[] Powers(SegmentEvidence evidence)
        {
            ReadOnlySpan<byte> loudness = evidence.Loudness;
            double[] powers = new double[loudness.Length];

            for (int at = 0; at < loudness.Length; at++)
            {
                powers[at] = Math.Pow(10, -SoundLoudness.DecibelsBelowFullScale(loudness[at]) / 10);
            }

            return powers;
        }

        private static double?[] Likeness(SegmentEvidence evidence)
        {
            ReadOnlySpan<ChannelDifference> channels = evidence.Channels;
            double?[] likeness = new double?[channels.Length];

            for (int at = 0; at < channels.Length; at++)
            {
                likeness[at] = channels[at].Likeness;
            }

            return likeness;
        }

        private double? LogoShare(Stretch piece)
        {
            if (sightings is null)
            {
                return null;
            }

            bool[] inside = sightings[Within(
                Math.Floor((piece.From + LogoAwayFromTheEdges).TotalSeconds) + 1,
                Math.Ceiling((piece.To - LogoAwayFromTheEdges).TotalSeconds),
                sightings.Length)];

            return inside.Length < FewestLogoSeconds ? null : inside.Count(seen => seen) / (double)inside.Length;
        }

        /// <summary>
        /// The decibels between the 10th and the 90th percentile of the momentary loudness inside
        /// <paramref name="piece"/>, away from its edges by at least half the silence at each, or over
        /// <see cref="ShortestLoudnessRange"/> around its middle when less than that lies there; not known when no window
        /// fits in the recording there.
        /// </summary>
        private double? LoudnessRange(Stretch piece, BoundaryCandidate head, BoundaryCandidate tail)
        {
            TimeSpan first = piece.From + Longer(LoudnessAwayFromTheEdges, head.SilenceLength / 2);
            TimeSpan last = piece.To - Longer(LoudnessAwayFromTheEdges, tail.SilenceLength / 2);

            if (last - first < ShortestLoudnessRange)
            {
                first = piece.Middle - (ShortestLoudnessRange / 2);
                last = piece.Middle + (ShortestLoudnessRange / 2);
            }

            int window = (int)Math.Round(MomentaryWindow / SegmentEvidence.LoudnessStep);
            int hop = (int)Math.Round(MomentaryHop / SegmentEvidence.LoudnessStep);
            Range inside = Within(Math.Ceiling(first / SegmentEvidence.LoudnessStep), Math.Floor(last / SegmentEvidence.LoudnessStep), power.Length);
            List<double> levels = [];

            for (int from = inside.Start.Value; from + window <= inside.End.Value; from += hop)
            {
                double sum = 0;

                foreach (double each in power.AsSpan(from, window))
                {
                    sum += each;
                }

                levels.Add(10 * Math.Log10(sum / window));
            }

            if (levels.Count is 0)
            {
                return null;
            }

            levels.Sort();

            return Percentile(levels, 0.9) - Percentile(levels, 0.1);
        }

        private static double Percentile(List<double> sorted, double share) => sorted[Math.Min(sorted.Count - 1, (int)(share * sorted.Count))];

        private double? OutsideGuide(Stretch piece)
        {
            if (context.ProgrammeStarts is null && context.ProgrammeEnds is null)
            {
                return null;
            }

            TimeSpan before = context.ProgrammeStarts is TimeSpan starts ? Clipped(piece.From, starts < piece.To ? starts : piece.To) : TimeSpan.Zero;
            TimeSpan after = context.ProgrammeEnds is TimeSpan ends ? Clipped(ends > piece.From ? ends : piece.From, piece.To) : TimeSpan.Zero;

            return (before + after) / piece.Length;
        }

        private double? PastShare(Func<PastEpisode, TimeSpan, bool> had, TimeSpan place)
            => context.Past.Count is 0 ? null : context.Past.Count(episode => had(episode, place)) / (double)context.Past.Count;

        private double? CaptionShare(Stretch piece)
        {
            if (captions is null)
            {
                return null;
            }

            bool[] inside = captions[Within(Centred(piece.From, TimeSpan.FromSeconds(1)), Centred(piece.To, TimeSpan.FromSeconds(1)), captions.Length)];

            return inside.Length is 0 ? null : inside.Count(shown => shown) / (double)inside.Length;
        }

        private double? MeanLikeness(TimeSpan from, TimeSpan to, int fewest = 1)
        {
            double[] measured =
            [
                .. likeness[Within(Centred(from, SegmentEvidence.ChannelStep), Centred(to, SegmentEvidence.ChannelStep), likeness.Length)]
                    .OfType<double>(),
            ];

            return measured.Length < Math.Max(fewest, 1) ? null : measured.Average();
        }

        private double? ChannelChangeAcross(TimeSpan at)
        {
            int fewest = (int)Math.Ceiling(ChannelWindow / SegmentEvidence.ChannelStep / 2);
            double? before = MeanLikeness(at - ChannelWindow, at, fewest);
            double? after = MeanLikeness(at, at + ChannelWindow, fewest);

            return after - before is double change ? Math.Abs(change) : null;
        }

        private static double Centred(TimeSpan at, TimeSpan step) => Math.Ceiling((at / step) - 0.5);

        private static Range Within(double first, double end, int count)
        {
            int from = (int)Math.Clamp(first, 0, count);

            return from..(int)Math.Clamp(end, from, count);
        }

        private static bool Audible(BoundaryCandidate candidate) => (candidate.Cues & ~BoundaryCue.LogoChange) is not BoundaryCue.None;

        private static TimeSpan Longer(TimeSpan one, TimeSpan other) => one > other ? one : other;

        private static TimeSpan Clipped(TimeSpan from, TimeSpan to) => to > from ? to - from : TimeSpan.Zero;

        private static double Between(TimeSpan at, TimeSpan earliest, TimeSpan latest) => at >= earliest && at <= latest ? 1 : 0;
    }

    /// <summary>
    /// The kind the catalog fixes a segment to: one whose stretches over it, of a known confidence, cover more than
    /// half of it, at the confidence of the surest of them; of two kinds that do, the surer, then the earlier kind;
    /// none when no kind does.
    /// </summary>
    private static CatalogFix? FixedBy(IReadOnlyList<CatalogHeard> catalog, Stretch piece)
        => catalog
            .Where(heard => heard is { Kind: SegmentKind, Confidence: > 0 } && heard.Starts < piece.To && heard.Ends > piece.From)
            .GroupBy(heard => heard.Kind!.Value)
            .Where(kind => Union([.. kind], piece) * 2 > piece.Length)
            .Select(kind => new CatalogFix(kind.Key, kind.Max(heard => heard.Confidence!.Value)))
            .OrderByDescending(fix => fix.Chance)
            .ThenBy(fix => fix.Kind)
            .FirstOrDefault();

    private static TimeSpan Union(CatalogHeard[] heard, Stretch piece)
    {
        TimeSpan total = TimeSpan.Zero;
        TimeSpan reached = piece.From;

        foreach (CatalogHeard stretch in heard.OrderBy(stretch => stretch.Starts))
        {
            TimeSpan from = stretch.Starts > reached ? stretch.Starts : reached;
            TimeSpan to = stretch.Ends < piece.To ? stretch.Ends : piece.To;

            if (to > from)
            {
                total += to - from;
                reached = to;
            }
        }

        return total;
    }

    private readonly record struct Stretch(TimeSpan From, TimeSpan To)
    {
        public TimeSpan Length => To - From;

        public TimeSpan Middle => From + ((To - From) / 2);
    }
}
