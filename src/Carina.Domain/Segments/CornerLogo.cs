namespace Carina.Domain.Segments;

/// <summary>
/// The corner outlines of one recording, one a second, beside what says which station it is of and
/// when it started.
/// </summary>
public sealed record RecordedCorners(SeriesMember Member, ReadOnlyMemory<byte> Outlines);

/// <summary>
/// The shapes a station's logo draws in the corner outlines, learned from the station's other
/// recordings that started before the one judged, never from the one judged. A block on in at least
/// <see cref="SteadyShare"/> of the seconds of one recording is steady there; the steady blocks of a
/// recording of at least <see cref="FewestSeconds"/> seconds are a shape when they number at least
/// <see cref="FewestBlocks"/> and cover no more than <see cref="MostOfTheCorners"/> of the corners.
/// Shapes sharing at least <see cref="SameShape"/> of their blocks are one shape, kept when at least
/// <see cref="FewestRecordings"/> recordings drew it, as the blocks on in at least half of them. A station
/// may keep several shapes, as its logo changes with the season. A second shows the logo when at least
/// <see cref="SeenShare"/> of the blocks of one of the shapes are on.
/// </summary>
public sealed class CornerLogo
{
    public const double SteadyShare = 0.5;

    public const int FewestSeconds = 60;

    public const int FewestBlocks = 4;

    public const double MostOfTheCorners = 0.1;

    public const double SameShape = 0.5;

    public const int FewestRecordings = 2;

    public const double SeenShare = 0.5;

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

        IEnumerable<bool[]> steady = recordings
            .Where(recording => IsEarlierOnTheStation(recording.Member, judged))
            .DistinctBy(recording => recording.Member.RecordingId)
            .OrderByDescending(recording => recording.Member.StartedAt)
            .ThenBy(recording => recording.Member.RecordingId.Value)
            .Select(recording => Steady(recording.Outlines.Span))
            .OfType<bool[]>();
        List<List<bool[]>> gathered = [];

        foreach (bool[] blocks in steady)
        {
            List<bool[]>? alike = gathered.FirstOrDefault(shape => Shared(shape[0], blocks) >= SameShape);

            if (alike is null)
            {
                gathered.Add([blocks]);
                continue;
            }

            alike.Add(blocks);
        }

        return new CornerLogo([.. gathered.Where(drawn => drawn.Count >= FewestRecordings).Select(Agreed).Where(shape => shape.Length >= FewestBlocks)]);
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

    private static bool IsEarlierOnTheStation(SeriesMember member, SeriesMember judged)
        => member.NetworkId.Equals(judged.NetworkId)
           && member.ServiceId.Equals(judged.ServiceId)
           && !member.RecordingId.Equals(judged.RecordingId)
           && member.StartedAt < judged.StartedAt;

    private static bool[]? Steady(ReadOnlySpan<byte> outlines)
    {
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

        return blocks >= FewestBlocks && blocks <= CornerOutline.Bits * MostOfTheCorners ? steady : null;
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

    private static double Shared(bool[] one, bool[] other)
    {
        int both = one.Where((block, at) => block && other[at]).Count();
        int either = one.Where((block, at) => block || other[at]).Count();

        return either is 0 ? 0 : both / (double)either;
    }

    private static int[] Agreed(List<bool[]> drawn)
        => [.. Enumerable.Range(0, CornerOutline.Bits).Where(block => drawn.Count(blocks => blocks[block]) * 2 >= drawn.Count)];

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
}
