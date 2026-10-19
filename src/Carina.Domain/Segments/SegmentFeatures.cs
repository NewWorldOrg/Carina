namespace Carina.Domain.Segments;

/// <summary>
/// A segment, the stretch between two neighbouring boundary candidates: where it starts and ends, its
/// length fitted to the grid from any of the cuts its edges took in, its features, and the kind the catalog
/// fixes it to, when it does (<see cref="SegmentSequence"/>).
/// </summary>
public sealed record SegmentPiece(TimeSpan Starts, TimeSpan Ends, TimeSpan Fitted, SegmentFeatureValues Features, SegmentKind? Fixed = null)
{
    public TimeSpan Length => Ends - Starts;
}

/// <summary>
/// Computes the features of each segment between neighbouring boundary candidates: its length, how far
/// it lies off the grid and whether it is a multiple of it; the depth and length of the silence at each
/// edge, and whether both edges are silent cuts; the share of the seconds inside it, away from its edges
/// by <see cref="LogoAwayFromTheEdges"/>, that show the logo, when at least
/// <see cref="FewestLogoSeconds"/> do; where it lies from
/// the programme's start and end, as shares of the programme's length, and the share of it outside the
/// guide; the share of earlier episodes with a break, and with an opening, within <see cref="PastWithin"/>
/// of the same place from their programme's start, and with an ending within it of the same place before
/// their programme's end; the share of its seconds with captions; the channels' mean likeness,
/// and how far it changes across each edge over <see cref="ChannelWindow"/> on either side, when at
/// least half of each window was measured; the changes of scene per ten seconds; the programme's first
/// genre; and a multiple of the grid without the logo. A feature without the readings to compute it
/// is not known. The programme spans the recording when the guide says nothing. The catalog is no feature:
/// it fixes a segment that its stretches of a break, an opening or an ending cover more than half of to that
/// kind, the surest of them, then the earlier kind.
/// </summary>
public static class SegmentFeatures
{
    public const int FewestLogoSeconds = 3;

    public static readonly TimeSpan LogoAwayFromTheEdges = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan PastWithin = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan ChannelWindow = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan SceneSpan = TimeSpan.FromSeconds(10);

    public static IReadOnlyList<SegmentPiece> Of(
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

        Reading reading = new(evidence, context, sightings, thresholds);

        return [.. candidates.Zip(candidates.Skip(1)).Select(pair => reading.Piece(pair.First, pair.Second))];
    }

    private sealed class Reading(SegmentEvidence evidence, SegmentContext context, bool[]? sightings, SegmentThresholds thresholds)
    {
        private readonly TimeSpan programmeStarts = context.ProgrammeStarts ?? TimeSpan.Zero;

        private readonly TimeSpan programmeEnds = context.ProgrammeEnds ?? evidence.Length;

        private readonly bool[]? captions = Shown(evidence);

        private readonly double?[] likeness = Likeness(evidence);

        private readonly List<TimeSpan> scenes = BoundaryCandidates.Scenes(evidence.Frames, thresholds);

        private readonly int? genre = context.Genres.Count is 0 ? null : context.Genres[0].Kind;

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
                [SegmentFeature.PastOpeningShare] = PastShare((episode, place) => episode.HadAnOpeningNear(place, PastWithin), piece.Middle - programmeStarts),
                [SegmentFeature.PastEndingShare] = PastShare((episode, place) => episode.HadAnEndingNear(place, PastWithin), programmeEnds - piece.Middle),
                [SegmentFeature.CaptionShare] = CaptionShare(piece),
                [SegmentFeature.ChannelCorrelation] = MeanLikeness(piece.From, piece.To),
                [SegmentFeature.HeadChannelChange] = ChannelChangeAcross(head.At),
                [SegmentFeature.TailChannelChange] = ChannelChangeAcross(tail.At),
                [SegmentFeature.SceneChangesPer10s] = scenes.Count(at => at >= piece.From && at < piece.To) * (SceneSpan / piece.Length),
                [SegmentFeature.MultipleOf15sWithoutLogo] = multiple * (1 - logo),
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

        private static TimeSpan Clipped(TimeSpan from, TimeSpan to) => to > from ? to - from : TimeSpan.Zero;
    }

    /// <summary>
    /// The kind the catalog fixes a segment to: a break, an opening or an ending whose stretches over it cover more
    /// than half of it, the surest of those that do, then the earlier kind; none when no such kind does.
    /// </summary>
    private static SegmentKind? FixedBy(IReadOnlyList<CatalogHeard> catalog, Stretch piece)
        => catalog
            .Where(heard => heard.Kind is SegmentKind.Break or SegmentKind.Opening or SegmentKind.Ending && heard.Starts < piece.To && heard.Ends > piece.From)
            .GroupBy(heard => heard.Kind!.Value)
            .Where(kind => Union([.. kind], piece) * 2 > piece.Length)
            .OrderByDescending(kind => kind.Max(heard => heard.Confidence ?? 0))
            .ThenBy(kind => kind.Key)
            .Select(kind => (SegmentKind?)kind.Key)
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
