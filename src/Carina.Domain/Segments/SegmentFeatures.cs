namespace Carina.Domain.Segments;

/// <summary>
/// A segment, the stretch between two neighbouring boundary candidates: where it starts and ends, its
/// length fitted to the grid from any of the cuts its edges took in, and its features.
/// </summary>
public sealed record SegmentPiece(TimeSpan Starts, TimeSpan Ends, TimeSpan Fitted, SegmentFeatureValues Features)
{
    public TimeSpan Length => Ends - Starts;
}

/// <summary>
/// Computes the features of each segment between neighbouring boundary candidates: its length, how far
/// it lies off the grid and whether it is a multiple of it; the depth and length of the silence at each
/// edge, and whether both edges are silent cuts; the share of the seconds inside it, away from its edges
/// by <see cref="LogoAwayFromTheEdges"/>, that show the logo, when at least
/// <see cref="FewestLogoSeconds"/> do; the share the catalog covers, in all and by the kind each stretch
/// counts as, and the most stretches, series and stations an entry over it is heard in; where it lies from
/// the programme's start and end, as shares of the programme's length, and the share of it outside the
/// guide; the share of earlier episodes with a break within <see cref="PastWithin"/> of the same place
/// from their programme's start; the share of its seconds with captions; the channels' mean likeness;
/// the programme's first genre; and the two interactions. A feature without the readings to compute it
/// is not known. The programme spans the recording when the guide says nothing.
/// </summary>
public static class SegmentFeatures
{
    public const int FewestLogoSeconds = 3;

    public static readonly TimeSpan LogoAwayFromTheEdges = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan PastWithin = TimeSpan.FromSeconds(30);

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
            Covered covered = Covered.By(context.Catalog, piece);
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
                [SegmentFeature.CatalogShare] = covered.Share(null),
                [SegmentFeature.CatalogCmShare] = covered.Share(SegmentKind.Break),
                [SegmentFeature.CatalogProgrammeShare] = covered.Share(SegmentKind.Programme),
                [SegmentFeature.CatalogOpeningShare] = covered.Share(SegmentKind.Opening),
                [SegmentFeature.CatalogEndingShare] = covered.Share(SegmentKind.Ending),
                [SegmentFeature.CatalogOccurrences] = covered.Occurrences,
                [SegmentFeature.CatalogSeries] = covered.Series,
                [SegmentFeature.CatalogStations] = covered.Stations,
                [SegmentFeature.FromStart] = (piece.From - programmeStarts) / ProgrammeLength,
                [SegmentFeature.FromEnd] = (programmeEnds - piece.To) / ProgrammeLength,
                [SegmentFeature.OutsideGuide] = OutsideGuide(piece),
                [SegmentFeature.PastCmShare] = PastShare(piece),
                [SegmentFeature.CaptionShare] = CaptionShare(piece),
                [SegmentFeature.ChannelCorrelation] = MeanLikeness(piece),
                [SegmentFeature.MultipleOf15sWithoutLogo] = multiple * (1 - logo),
                [SegmentFeature.CatalogCmMultipleOf15s] = covered.Share(SegmentKind.Break) * multiple,
            };

            for (int kind = 0; kind < SegmentFeature.GenreKinds; kind++)
            {
                features[SegmentFeature.Genre(kind)] = genre is null ? null : (genre == kind ? 1 : 0);
            }

            return new SegmentPiece(piece.From, piece.To, fitted, SegmentFeatureValues.Of(features));
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

        private double? PastShare(Stretch piece)
        {
            if (context.Past.Count is 0)
            {
                return null;
            }

            TimeSpan place = piece.Middle - programmeStarts;

            return context.Past.Count(episode => episode.HadABreakNear(place, PastWithin)) / (double)context.Past.Count;
        }

        private double? CaptionShare(Stretch piece)
        {
            if (captions is null)
            {
                return null;
            }

            bool[] inside = captions[Within(Centred(piece.From, TimeSpan.FromSeconds(1)), Centred(piece.To, TimeSpan.FromSeconds(1)), captions.Length)];

            return inside.Length is 0 ? null : inside.Count(shown => shown) / (double)inside.Length;
        }

        private double? MeanLikeness(Stretch piece)
        {
            double[] measured =
            [
                .. likeness[Within(Centred(piece.From, SegmentEvidence.ChannelStep), Centred(piece.To, SegmentEvidence.ChannelStep), likeness.Length)]
                    .OfType<double>(),
            ];

            return measured.Length is 0 ? null : measured.Average();
        }

        private static double Centred(TimeSpan at, TimeSpan step) => Math.Ceiling((at / step) - 0.5);

        private static Range Within(double first, double end, int count)
        {
            int from = (int)Math.Clamp(first, 0, count);

            return from..(int)Math.Clamp(end, from, count);
        }

        private static TimeSpan Clipped(TimeSpan from, TimeSpan to) => to > from ? to - from : TimeSpan.Zero;
    }

    private readonly record struct Stretch(TimeSpan From, TimeSpan To)
    {
        public TimeSpan Length => To - From;

        public TimeSpan Middle => From + ((To - From) / 2);
    }

    private sealed class Covered
    {
        private readonly Dictionary<SegmentKind, TimeSpan> byKind = [];

        private TimeSpan all;

        private TimeSpan length;

        public double Occurrences { get; private set; }

        public double Series { get; private set; }

        public double Stations { get; private set; }

        public static Covered By(IReadOnlyList<CatalogHeard> catalog, Stretch piece)
        {
            CatalogHeard[] over = [.. catalog.Where(heard => heard.Starts < piece.To && heard.Ends > piece.From)];
            Covered covered = new()
            {
                length = piece.Length,
                all = Union(over, piece),
                Occurrences = over.Select(heard => heard.Occurrences).DefaultIfEmpty(0).Max(),
                Series = over.Select(heard => heard.Series).DefaultIfEmpty(0).Max(),
                Stations = over.Select(heard => heard.Stations).DefaultIfEmpty(0).Max(),
            };

            foreach (SegmentKind kind in Enum.GetValues<SegmentKind>())
            {
                covered.byKind[kind] = Union([.. over.Where(heard => heard.Kind == kind)], piece);
            }

            return covered;
        }

        public double Share(SegmentKind? kind) => (kind is SegmentKind counted ? byKind[counted] : all) / length;

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
    }
}
