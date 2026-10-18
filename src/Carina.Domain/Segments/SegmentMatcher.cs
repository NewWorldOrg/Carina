using System.Numerics;

namespace Carina.Domain.Segments;

/// <summary>
/// Finds the stretches of one recording that sound the same as stretches of another recording, of a
/// catalog entry's fingerprint, or of itself elsewhere. The recording heard is indexed once, from each
/// fingerprint value to the frames it is heard at. Every frame of the other sound whose value is heard
/// somewhere in the recording votes for the offset between the two, in frames; an offset that gathers
/// <see cref="LeastVotes"/> votes together with the offsets on either side of it is a candidate. The
/// frames voting for a candidate are cut into stretches where they lie no more than two seconds apart,
/// and each stretch is widened both ways, a frame at a time, while the share of disagreeing bits over the
/// second centred on the next frame stays under <see cref="MostBitErrors"/>; it is not widened into a frame
/// that is silence on either side, and the silence at its ends is cut off, so that a stretch between two
/// silences is as long as the sound between them. A stretch whose votes mostly fall on silence or on
/// a sound that repeats itself within a second, one shorter than <see cref="Shortest"/> or longer than
/// <see cref="Longest"/>, and one whose bits disagree that much over its whole length are not matches.
/// Silence, and a value heard at more than <see cref="MostPlacesOfOneValue"/> frames of the recording,
/// cast no vote: neither says where a match lies.
/// </summary>
public sealed class SegmentMatcher
{
    public const int LeastVotes = 250;

    public const double MostBitErrors = 0.3;

    public const int MostPlacesOfOneValue = 64;

    public const int MostFrames = 11_250;

    public static readonly TimeSpan Frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond * SoundFingerprint.Hop / SoundReader.SampleRate);

    public static readonly TimeSpan Shortest = TimeSpan.FromSeconds(4);

    public static readonly TimeSpan Longest = Frame * MostFrames;

    private const int HalfWindow = 31;

    private const int GapFrames = 125;

    private const int RepeatFrames = 62;

    private const double MostQuietShare = 0.5;

    private const int Bits = 32;

    private static readonly int ShortestFrames = FrameAt(Shortest);

    private readonly SoundTrack heard;

    private readonly uint[] fingerprints;

    private readonly FingerprintIndex index;

    private SegmentMatcher(SoundTrack heard)
    {
        this.heard = heard;
        fingerprints = heard.Fingerprints.ToArray();
        index = FingerprintIndex.Of(fingerprints, MostPlacesOfOneValue);
    }

    public static SegmentMatcher For(SoundTrack heard)
    {
        ArgumentNullException.ThrowIfNull(heard);

        return new SegmentMatcher(heard);
    }

    public static int FrameAt(TimeSpan time) => (int)Math.Round((double)time.Ticks / Frame.Ticks, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The stretches of the recording heard that sound the same as stretches of
    /// <paramref name="other"/>, whose fingerprints start at its own time zero. The stretches of the
    /// other sound in <paramref name="explained"/> are neither voted from nor matched into.
    /// </summary>
    public IReadOnlyList<SoundMatch> Against(SoundOrigin other, ReadOnlySpan<uint> there, IReadOnlyList<SoundSpan> explained)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(explained);

        return Matched(other, there, Masked(there.Length, explained), false);
    }

    /// <summary>
    /// The stretches of the recording heard that sound the same as a later stretch of itself which
    /// they do not overlap, each found once, from its earlier side.
    /// </summary>
    public IReadOnlyList<SoundMatch> AgainstItself(SoundOrigin itself)
    {
        ArgumentNullException.ThrowIfNull(itself);

        return Matched(itself, fingerprints, null, true);
    }

    private static bool[]? Masked(int frames, IReadOnlyList<SoundSpan> explained)
    {
        if (explained.Count is 0)
        {
            return null;
        }

        bool[] masked = new bool[frames];

        foreach (SoundSpan span in explained)
        {
            int from = Math.Clamp(FrameAt(span.Starts), 0, frames);
            int to = Math.Clamp(FrameAt(span.Ends), from, frames);

            masked.AsSpan(from, to - from).Fill(true);
        }

        return masked;
    }

    private List<SoundMatch> Matched(SoundOrigin other, ReadOnlySpan<uint> there, bool[]? masked, bool itself)
    {
        List<long> votes = Votes(there, masked, itself);
        List<Stretch> stretches = [];

        votes.Sort();

        foreach (Candidate candidate in Candidates(votes))
        {
            foreach (int[] cluster in Clusters(candidate.Places))
            {
                if (Widened(there, masked, candidate.Offset, cluster, itself) is Stretch stretch)
                {
                    stretches.Add(stretch);
                }
            }
        }

        return
        [
            .. Distinct(stretches)
                .OrderBy(stretch => stretch.Starts)
                .Select(stretch => new SoundMatch(
                    new SoundSpan(Frame * stretch.Starts, Frame * stretch.Ends),
                    other,
                    new SoundSpan(Frame * (stretch.Starts + stretch.Offset), Frame * (stretch.Ends + stretch.Offset)),
                    stretch.BitErrorRate)),
        ];
    }

    private List<long> Votes(ReadOnlySpan<uint> there, bool[]? masked, bool itself)
    {
        List<long> votes = [];
        ReadOnlySpan<int> places = index.Places;

        for (int frame = 0; frame < there.Length; frame++)
        {
            if (there[frame] == 0 || masked?[frame] is true || !index.TryFind(there[frame], out int first, out int count))
            {
                continue;
            }

            foreach (int place in places.Slice(first, count))
            {
                int offset = frame - place;

                if (!itself || offset >= ShortestFrames)
                {
                    votes.Add(((long)offset << 32) | (uint)place);
                }
            }
        }

        return votes;
    }

    /// <summary>
    /// The offsets that gather enough votes with their neighbours, the most voted first and, of two that
    /// gather as many, the one with more votes of its own; an offset taken takes its neighbours' votes with
    /// it, so that each vote counts towards one candidate.
    /// </summary>
    private static IEnumerable<Candidate> Candidates(List<long> votes)
    {
        List<OffsetVotes> offsets = Offsets(votes);
        bool[] taken = new bool[offsets.Count];
        int[] order =
        [
            .. Enumerable.Range(0, offsets.Count)
                .OrderByDescending(at => Gathered(offsets, taken, at))
                .ThenByDescending(at => offsets[at].Count)
                .ThenBy(at => offsets[at].Offset),
        ];

        foreach (int at in order)
        {
            if (taken[at] || Gathered(offsets, taken, at) < LeastVotes)
            {
                continue;
            }

            int[] neighbours = [.. Neighbours(offsets, at).Where(near => !taken[near])];

            foreach (int near in neighbours)
            {
                taken[near] = true;
            }

            int[] places = [.. neighbours.SelectMany(near => Places(votes, offsets[near])).Order()];

            yield return new Candidate(offsets[at].Offset, places);
        }
    }

    private static List<OffsetVotes> Offsets(List<long> votes)
    {
        List<OffsetVotes> offsets = [];

        for (int at = 0; at < votes.Count;)
        {
            int offset = (int)(votes[at] >> 32);
            int from = at;

            while (at < votes.Count && (int)(votes[at] >> 32) == offset)
            {
                at++;
            }

            offsets.Add(new OffsetVotes(offset, from, at - from));
        }

        return offsets;
    }

    private static int Gathered(List<OffsetVotes> offsets, bool[] taken, int at)
        => Neighbours(offsets, at).Where(near => !taken[near]).Sum(near => offsets[near].Count);

    private static IEnumerable<int> Neighbours(List<OffsetVotes> offsets, int at)
    {
        if (at > 0 && offsets[at - 1].Offset == offsets[at].Offset - 1)
        {
            yield return at - 1;
        }

        yield return at;

        if (at + 1 < offsets.Count && offsets[at + 1].Offset == offsets[at].Offset + 1)
        {
            yield return at + 1;
        }
    }

    private static IEnumerable<int> Places(List<long> votes, OffsetVotes offset)
        => Enumerable.Range(offset.From, offset.Count).Select(at => (int)(uint)votes[at]);

    private static IEnumerable<int[]> Clusters(int[] places)
    {
        int from = 0;

        for (int at = 1; at <= places.Length; at++)
        {
            if (at == places.Length || places[at] - places[at - 1] > GapFrames)
            {
                yield return places[from..at];
                from = at;
            }
        }
    }

    private Stretch? Widened(ReadOnlySpan<uint> there, bool[]? masked, int offset, int[] votes, bool itself)
    {
        Diagonal diagonal = new(fingerprints, Math.Max(0, -offset), Math.Min(fingerprints.Length, there.Length - offset), offset);
        int starts = Math.Max(votes[0], diagonal.Low);
        int ends = Math.Min(itself ? Math.Min(votes[^1] + 1, starts + offset) : votes[^1] + 1, diagonal.High);

        if (ends <= starts || ends - starts > MostFrames || diagonal.Errors(there, masked, starts, ends) >= MostBitErrors)
        {
            return null;
        }

        while (starts > diagonal.Low && Reaches(there, masked, diagonal, starts - 1, itself) && (!itself || ends <= starts - 1 + offset) && ends - starts <= MostFrames)
        {
            starts--;
        }

        while (ends < diagonal.High && Reaches(there, masked, diagonal, ends, itself) && (!itself || ends + 1 <= starts + offset) && ends - starts <= MostFrames)
        {
            ends++;
        }

        while (starts < ends && Silent(there, diagonal, starts, itself))
        {
            starts++;
        }

        while (ends > starts && Silent(there, diagonal, ends - 1, itself))
        {
            ends--;
        }

        return Kept(there, masked, diagonal, starts, ends, votes);
    }

    private bool Reaches(ReadOnlySpan<uint> there, bool[]? masked, Diagonal diagonal, int frame, bool itself)
        => !Silent(there, diagonal, frame, itself) && diagonal.Agrees(there, masked, frame);

    /// <summary>
    /// Whether a frame of the stretch is silence on either side: the recording heard is silent there, or
    /// either fingerprint has no bit set, as silence has none.
    /// </summary>
    private bool Silent(ReadOnlySpan<uint> there, Diagonal diagonal, int frame, bool itself)
    {
        int other = frame + diagonal.Offset;

        return fingerprints[frame] == 0
               || there[other] == 0
               || heard.IsSilentAt(frame)
               || (itself && heard.IsSilentAt(other));
    }

    private Stretch? Kept(ReadOnlySpan<uint> there, bool[]? masked, Diagonal diagonal, int starts, int ends, int[] votes)
    {
        if (ends - starts < ShortestFrames || ends - starts > MostFrames || QuietShare(votes) > MostQuietShare)
        {
            return null;
        }

        double errors = diagonal.Errors(there, masked, starts, ends);

        return errors < MostBitErrors ? new Stretch(starts, ends, diagonal.Offset, votes.Length, errors) : null;
    }

    private double QuietShare(int[] votes) => votes.Count(place => heard.IsSilentAt(place) || RepeatsWithinASecond(place)) / (double)votes.Length;

    private bool RepeatsWithinASecond(int place)
        => fingerprints.AsSpan(Math.Max(0, place - RepeatFrames), Math.Min(place, RepeatFrames)).Contains(fingerprints[place]);

    private static IEnumerable<Stretch> Distinct(List<Stretch> stretches)
    {
        List<Stretch> kept = [];

        foreach (Stretch stretch in stretches.OrderByDescending(stretch => stretch.Votes).ThenBy(stretch => stretch.Starts))
        {
            if (!kept.Any(other => other.Covers(stretch)))
            {
                kept.Add(stretch);
            }
        }

        return kept;
    }

    private sealed record Candidate(int Offset, int[] Places);

    private sealed record OffsetVotes(int Offset, int From, int Count);

    private sealed record Stretch(int Starts, int Ends, int Offset, int Votes, double BitErrorRate)
    {
        public bool Covers(Stretch other)
            => Shares(Starts, Ends, other.Starts, other.Ends)
               && Shares(Starts + Offset, Ends + Offset, other.Starts + other.Offset, other.Ends + other.Offset);

        private static bool Shares(int starts, int ends, int otherStarts, int otherEnds)
            => 2 * (Math.Min(ends, otherEnds) - Math.Max(starts, otherStarts)) >= Math.Min(ends - starts, otherEnds - otherStarts);
    }

    /// <summary>
    /// The frames of the recording heard lined up with the frames of the other sound at one offset, from
    /// <see cref="Low"/> up to <see cref="High"/>; a frame of the other sound already explained, or out
    /// of reach, disagrees in half of its bits.
    /// </summary>
    private sealed record Diagonal(uint[] Heard, int Low, int High, int Offset)
    {
        public bool Agrees(ReadOnlySpan<uint> there, bool[]? masked, int frame)
            => masked?[frame + Offset] is not true
               && Errors(there, masked, Math.Max(Low, frame - HalfWindow), Math.Min(High, frame + HalfWindow + 1)) < MostBitErrors;

        public double Errors(ReadOnlySpan<uint> there, bool[]? masked, int starts, int ends)
        {
            long disagreeing = 0;

            for (int frame = starts; frame < ends; frame++)
            {
                disagreeing += masked?[frame + Offset] is true ? Bits / 2 : BitOperations.PopCount(Heard[frame] ^ there[frame + Offset]);
            }

            return disagreeing / (double)(Bits * (ends - starts));
        }
    }
}
