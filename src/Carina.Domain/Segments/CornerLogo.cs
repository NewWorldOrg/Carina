namespace Carina.Domain.Segments;

/// <summary>
/// The corner outlines of one recording, one a second, beside what says which station it is of and
/// when it started.
/// </summary>
public sealed record RecordedCorners(SeriesMember Member, ReadOnlyMemory<byte> Outlines);

/// <summary>
/// The shapes a station's logo draws in the corner outlines, learned from the station's other recordings that
/// started before the one judged, never from the one judged. A block on in at least <see cref="SteadyShare"/>
/// of the seconds of a recording of at least <see cref="FewestSeconds"/> seconds is steady there; a recording
/// with fewer than <see cref="FewestBlocks"/> steady blocks shows no logo. A recording whose steady blocks
/// cover more than <see cref="MostOfTheCorners"/> of the corners carries a still picture besides any logo: it
/// may agree with a shape another recording draws and count towards its blocks, but a block only such
/// recordings hold steady is never part of a shape. Each recording, newest first and those carrying a still
/// picture last, joins the first shape it agrees with by at least <see cref="SameShape"/>
/// (<see cref="Agreement"/>), or else draws a new one. A shape is kept when at least
/// <see cref="FewestRecordings"/> broadcasts agree on it, two recordings of one broadcast counting once
/// (<see cref="SeriesMember.Episode"/>), as the blocks steady in at least that many of those broadcasts and
/// in at least half of them, and when it then has from <see cref="FewestBlocks"/> blocks to
/// <see cref="MostOfTheCorners"/> of the corners. A station may keep several shapes, as its logo changes with
/// the season. A second shows the logo when at least <see cref="SeenShare"/> of the blocks of one of the
/// shapes are on. A logo seen in at least <see cref="NearlyEverywhere"/> of a recording's seconds, or in no
/// more than <see cref="NearlyNowhere"/> of them, tells programme from break nowhere in it.
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

        SteadyBlocks[] steady =
        [
            .. recordings
                .Where(recording => IsEarlierOnTheStation(recording.Member, judged))
                .DistinctBy(recording => recording.Member.RecordingId)
                .OrderByDescending(recording => recording.Member.StartedAt)
                .ThenBy(recording => recording.Member.RecordingId.Value)
                .Select(Steady)
                .OfType<SteadyBlocks>(),
        ];
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

        return new CornerLogo([.. gathered.Select(Agreed).OfType<int[]>()]);
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

        bool[] steady = [.. on.Select(count => count >= seconds * SteadyShare)];
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
        int[] shape = [.. Enumerable.Range(0, CornerOutline.Bits).Where(block => IsAgreed(drawn, broadcasts, block))];

        return broadcasts >= FewestRecordings && shape.Length >= FewestBlocks && !CoversTooMuch(shape.Length) ? shape : null;
    }

    private static bool IsAgreed(List<SteadyBlocks> drawn, int broadcasts, int block)
    {
        int holding = drawn.Where(blocks => blocks.Blocks[block]).Select(blocks => blocks.Episode).Distinct().Count();

        return holding >= FewestRecordings && holding * 2 >= broadcasts && drawn.Any(blocks => !blocks.IsStill && blocks.Blocks[block]);
    }

    private static bool CoversTooMuch(int blocks) => blocks > CornerOutline.Bits * MostOfTheCorners;

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
    private sealed record SteadyBlocks(SeriesEpisode Episode, bool[] Blocks, int Count)
    {
        public bool IsStill => CoversTooMuch(Count);

        public double Covered(int corner)
            => Blocks.Skip(corner * CornerOutline.BlocksInACorner).Take(CornerOutline.BlocksInACorner).Count(block => block) / (double)CornerOutline.BlocksInACorner;
    }
}
