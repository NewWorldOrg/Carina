namespace Carina.Domain.Segments;

/// <summary>
/// The corner outlines of one recording, one a second, beside what says which station it is of and
/// when it started, and the stretches the catalog heard in it, when they are known.
/// </summary>
public sealed record RecordedCorners(SeriesMember Member, ReadOnlyMemory<byte> Outlines, IReadOnlyList<CatalogHeard>? Catalog = null);

/// <summary>
/// The shapes a station's logo draws in the corner outlines, learned from the station's other recordings that
/// started before the one judged, never from the one judged. A block on in at least <see cref="SteadyShare"/>
/// of the seconds of a recording of at least <see cref="FewestSeconds"/> seconds is steady there, save one on
/// the outermost column or row of the picture, where the edge of a letterboxed or pillarboxed picture lies; a
/// recording with fewer than <see cref="FewestBlocks"/> steady blocks shows no logo. A recording whose steady
/// blocks cover more than <see cref="MostOfTheCorners"/> of the corners carries a still picture besides any
/// logo: it may agree with a shape another recording draws and count towards its blocks, but a block only
/// such recordings hold steady is never part of a shape. Each recording, newest first and those carrying a
/// still picture last, joins the first shape it agrees with by at least <see cref="SameShape"/>
/// (<see cref="Agreement"/>), or else draws a new one. A shape is kept when at least
/// <see cref="FewestRecordings"/> broadcasts agree on it, two recordings of one broadcast counting once
/// (<see cref="SeriesMember.Episode"/>), as the blocks steady in at least that many of those broadcasts and
/// in at least half of them, less any whose mirror across the middle of the picture is kept too, as the
/// borders of a letterboxed picture are; and when it then has from <see cref="FewestBlocks"/> blocks to
/// <see cref="MostOfTheCorners"/> of the corners. A shape is kept only when, in those recordings, the share
/// of the seconds the catalog heard as a break, away from their edges by
/// <see cref="SegmentFeatures.LogoAwayFromTheEdges"/>, that show it is at most <see cref="MostSeenInBreaks"/>
/// of the share of the rest of their seconds that show it; with nothing heard as a break, nothing says
/// otherwise. A station may keep several shapes, as its logo changes with the season. A second shows the logo
/// when at least <see cref="SeenShare"/> of the blocks of one of the shapes are on. A logo seen in at least
/// <see cref="NearlyEverywhere"/> of a recording's seconds, or in no more than <see cref="NearlyNowhere"/> of
/// them, tells programme from break nowhere in it.
/// </summary>
public sealed class CornerLogo
{
    public const double SteadyShare = 0.4;

    public const int FewestSeconds = 60;

    public const int FewestBlocks = 4;

    public const double MostOfTheCorners = 0.1;

    public const double SameShape = 0.5;

    public const int FewestRecordings = 2;

    public const double SeenShare = 0.5;

    public const double MostSeenInBreaks = 0.3;

    public const double NearlyEverywhere = 0.95;

    public const double NearlyNowhere = 0.05;

    private readonly int[][] shapes;

    private CornerLogo(int[][] shapes)
    {
        this.shapes = shapes;
        Shapes = [.. shapes.Select(shape => Array.AsReadOnly(shape))];
    }

    public static CornerLogo None { get; } = new([]);

    public IReadOnlyList<IReadOnlyList<int>> Shapes { get; }

    public static CornerLogo Learn(SeriesMember judged, IEnumerable<RecordedCorners> recordings)
    {
        ArgumentNullException.ThrowIfNull(judged);
        ArgumentNullException.ThrowIfNull(recordings);

        RecordedCorners[] earlier =
        [
            .. recordings
                .Where(recording => IsEarlierOnTheStation(recording.Member, judged))
                .DistinctBy(recording => recording.Member.RecordingId)
                .OrderByDescending(recording => recording.Member.StartedAt)
                .ThenBy(recording => recording.Member.RecordingId.Value),
        ];
        SteadyBlocks[] steady = [.. earlier.Select(Steady).OfType<SteadyBlocks>()];
        List<List<SteadyBlocks>> gathered = [];

        foreach (SteadyBlocks blocks in steady.Where(blocks => !blocks.IsStill).Concat(steady.Where(blocks => blocks.IsStill)))
        {
            List<SteadyBlocks>? alike = gathered.FirstOrDefault(shape => Agreement(shape[0], blocks) >= SameShape);

            if (alike is not null)
            {
                alike.Add(blocks);
            }
            else if (!blocks.IsStill)
            {
                gathered.Add([blocks]);
            }
        }

        return new CornerLogo([.. gathered.Select(Agreed).OfType<int[]>().Where(shape => TellsBreaks(shape, earlier))]);
    }

    /// <summary>
    /// Whether each second of <paramref name="outlines"/> shows the logo.
    /// </summary>
    public bool[] Sightings(ReadOnlySpan<byte> outlines)
    {
        if (outlines.Length % CornerOutline.Bytes is not 0)
        {
            throw new ArgumentException($"Corner outlines come {CornerOutline.Bytes} bytes a second.", nameof(outlines));
        }

        bool[] seen = new bool[outlines.Length / CornerOutline.Bytes];

        for (int second = 0; second < seen.Length; second++)
        {
            seen[second] = ShowsAny(outlines.Slice(second * CornerOutline.Bytes, CornerOutline.Bytes));
        }

        return seen;
    }

    /// <summary>
    /// Whether each second of <paramref name="outlines"/> shows the logo, when that tells programme from
    /// break; null when there is no logo, nothing to look at, or it is seen nearly everywhere or nowhere.
    /// </summary>
    public bool[]? Telling(ReadOnlySpan<byte> outlines)
    {
        if (shapes.Length is 0 || outlines.IsEmpty)
        {
            return null;
        }

        bool[] seen = Sightings(outlines);
        double share = seen.Count(second => second) / (double)seen.Length;

        return share is >= NearlyEverywhere or <= NearlyNowhere ? null : seen;
    }

    private static bool IsEarlierOnTheStation(SeriesMember member, SeriesMember judged)
        => member.NetworkId.Equals(judged.NetworkId)
           && member.ServiceId.Equals(judged.ServiceId)
           && !member.RecordingId.Equals(judged.RecordingId)
           && member.StartedAt < judged.StartedAt;

    private static SteadyBlocks? Steady(RecordedCorners recording)
    {
        ReadOnlySpan<byte> outlines = recording.Outlines.Span;
        int seconds = outlines.Length / CornerOutline.Bytes;

        if (outlines.Length % CornerOutline.Bytes is not 0 || seconds < FewestSeconds)
        {
            return null;
        }

        int[] on = new int[CornerOutline.Bits];

        for (int second = 0; second < seconds; second++)
        {
            Count(outlines.Slice(second * CornerOutline.Bytes, CornerOutline.Bytes), on);
        }

        bool[] steady = [.. on.Select((count, block) => count >= seconds * SteadyShare && !IsOnTheEdge(block))];
        int blocks = steady.Count(block => block);

        return blocks >= FewestBlocks ? new SteadyBlocks(recording.Member.Episode, steady, blocks) : null;
    }

    private static void Count(ReadOnlySpan<byte> outline, int[] on)
    {
        for (int block = 0; block < CornerOutline.Bits; block++)
        {
            if (IsOn(outline, block))
            {
                on[block]++;
            }
        }
    }

    /// <summary>
    /// How far the smaller of two sets of steady blocks lies inside the larger beyond chance: the blocks they
    /// share, less those the smaller would share by chance given how much of each corner the larger covers,
    /// over the smaller's blocks less that chance. A logo inside a still picture agrees fully with the same
    /// logo alone, however much more the picture covers, while a picture busy all over a corner agrees with
    /// nothing there by chance alone.
    /// </summary>
    private static double Agreement(SteadyBlocks one, SteadyBlocks other)
    {
        (SteadyBlocks smaller, SteadyBlocks larger) = one.Count <= other.Count ? (one, other) : (other, one);
        double[] covered = [.. Enumerable.Range(0, CornerOutline.Corners).Select(larger.Covered)];
        int[] blocks = [.. Enumerable.Range(0, CornerOutline.Bits).Where(block => smaller.Blocks[block])];
        double chance = blocks.Sum(block => covered[block / CornerOutline.BlocksInACorner]);
        int shared = blocks.Count(block => larger.Blocks[block]);

        return blocks.Length - chance <= 0 ? 0 : (shared - chance) / (blocks.Length - chance);
    }

    private static int[]? Agreed(List<SteadyBlocks> drawn)
    {
        int broadcasts = drawn.Select(blocks => blocks.Episode).Distinct().Count();
        bool[] agreed = [.. Enumerable.Range(0, CornerOutline.Bits).Select(block => IsAgreed(drawn, broadcasts, block))];
        int[] shape = [.. Enumerable.Range(0, CornerOutline.Bits).Where(block => agreed[block] && !agreed[Mirrored(block)])];

        return broadcasts >= FewestRecordings && shape.Length >= FewestBlocks && !CoversTooMuch(shape.Length) ? shape : null;
    }

    private static bool IsAgreed(List<SteadyBlocks> drawn, int broadcasts, int block)
    {
        int holding = drawn.Where(blocks => blocks.Blocks[block]).Select(blocks => blocks.Episode).Distinct().Count();

        return holding >= FewestRecordings && holding * 2 >= broadcasts && drawn.Any(blocks => !blocks.IsStill && blocks.Blocks[block]);
    }

    private static bool TellsBreaks(int[] shape, RecordedCorners[] recordings)
    {
        Tally[] tallies = [.. recordings.Select(recording => Tallied(shape, recording))];
        long breaks = tallies.Sum(tally => (long)tally.Breaks);
        long rest = tallies.Sum(tally => (long)tally.Rest);

        return breaks is 0 || rest is 0
            || tallies.Sum(tally => (long)tally.SeenInBreaks) * rest <= MostSeenInBreaks * tallies.Sum(tally => (long)tally.SeenInRest) * breaks;
    }

    private static Tally Tallied(int[] shape, RecordedCorners recording)
    {
        ReadOnlySpan<byte> outlines = recording.Outlines.Span;

        if (outlines.Length % CornerOutline.Bytes is not 0)
        {
            return new Tally(0, 0, 0, 0);
        }

        int seconds = outlines.Length / CornerOutline.Bytes;
        CatalogHeard[] breaks = [.. (recording.Catalog ?? []).Where(heard => heard.Kind is SegmentKind.Break)];
        bool[] heard = Marked(seconds, breaks.Select(stretch => (stretch.Starts, stretch.Ends)));
        bool[] inside = Marked(
            seconds,
            breaks.Select(stretch => (stretch.Starts + SegmentFeatures.LogoAwayFromTheEdges + TimeSpan.FromSeconds(1), stretch.Ends - SegmentFeatures.LogoAwayFromTheEdges)));
        int inBreaks = 0;
        int seenInBreaks = 0;
        int rest = 0;
        int seenInRest = 0;

        for (int second = 0; second < seconds; second++)
        {
            int seen = Shows(outlines.Slice(second * CornerOutline.Bytes, CornerOutline.Bytes), shape) ? 1 : 0;

            if (inside[second])
            {
                inBreaks++;
                seenInBreaks += seen;
            }
            else if (!heard[second])
            {
                rest++;
                seenInRest += seen;
            }
        }

        return new Tally(inBreaks, seenInBreaks, rest, seenInRest);
    }

    private static bool[] Marked(int seconds, IEnumerable<(TimeSpan From, TimeSpan To)> stretches)
    {
        bool[] marked = new bool[seconds];

        foreach ((TimeSpan from, TimeSpan to) in stretches)
        {
            int first = (int)Math.Clamp(Math.Floor(from.TotalSeconds), 0, seconds);
            int last = (int)Math.Clamp(Math.Ceiling(to.TotalSeconds), first, seconds);

            marked.AsSpan(first, last - first).Fill(true);
        }

        return marked;
    }

    private static bool CoversTooMuch(int blocks) => blocks > CornerOutline.Bits * MostOfTheCorners;

    private static bool IsOnTheEdge(int block)
    {
        int corner = block / CornerOutline.BlocksInACorner;
        int row = block % CornerOutline.BlocksInACorner / CornerOutline.Columns;
        int column = block % CornerOutline.Columns;

        return column == (corner % 2 is 0 ? 0 : CornerOutline.Columns - 1) || row == (corner / 2 is 0 ? 0 : CornerOutline.Rows - 1);
    }

    private static int Mirrored(int block)
    {
        int corner = block / CornerOutline.BlocksInACorner;
        int row = block % CornerOutline.BlocksInACorner / CornerOutline.Columns;
        int column = block % CornerOutline.Columns;

        return ((corner ^ 1) * CornerOutline.BlocksInACorner) + (row * CornerOutline.Columns) + (CornerOutline.Columns - 1 - column);
    }

    private bool ShowsAny(ReadOnlySpan<byte> outline)
    {
        foreach (int[] shape in shapes)
        {
            if (Shows(outline, shape))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Shows(ReadOnlySpan<byte> outline, int[] shape)
    {
        int on = 0;

        foreach (int block in shape)
        {
            if (IsOn(outline, block))
            {
                on++;
            }
        }

        return on >= shape.Length * SeenShare;
    }

    private static bool IsOn(ReadOnlySpan<byte> outline, int block) => (outline[block >> 3] & (1 << (block & 7))) is not 0;

    /// <summary>
    /// The steady blocks of one recording, with the broadcast it is of; a recording whose steady blocks cover
    /// too much of the corners carries a still picture.
    /// </summary>
    /// <summary>
    /// The seconds of one recording inside what the catalog heard as a break and the rest of its seconds, each with
    /// how many of them show a shape.
    /// </summary>
    private readonly record struct Tally(int Breaks, int SeenInBreaks, int Rest, int SeenInRest);

    private sealed record SteadyBlocks(SeriesEpisode Episode, bool[] Blocks, int Count)
    {
        public bool IsStill => CoversTooMuch(Count);

        public double Covered(int corner)
            => Blocks.Skip(corner * CornerOutline.BlocksInACorner).Take(CornerOutline.BlocksInACorner).Count(block => block) / (double)CornerOutline.BlocksInACorner;
    }
}
