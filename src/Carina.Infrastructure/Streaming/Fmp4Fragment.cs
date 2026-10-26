using System.Buffers.Binary;
using System.Numerics;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// What a track declares of its samples in the initialisation, for a fragment that leaves them unsaid.
/// </summary>
public sealed record Fmp4TrackDefaults(uint Timescale, uint Duration, uint Size, uint Flags);

public readonly record struct Fmp4Sample(uint Duration, uint Size, uint Flags, uint CompositionOffset)
{
    private const uint NotASyncSample = 0x00010000;

    public bool IsASync => (Flags & NotASyncSample) is 0U;
}

/// <summary>
/// One track's part of a fragment: when its first sample is decoded, its samples, and their bytes one after another.
/// </summary>
public sealed record Fmp4Run(uint Track, ulong DecodeTime, IReadOnlyList<Fmp4Sample> Samples, ReadOnlyMemory<byte> Data, bool SignedOffsets)
{
    public ulong Ticks => Samples.Aggregate(0UL, (sum, sample) => sum + sample.Duration);

    public ulong TicksBefore(int sample) => Samples.Take(sample).Aggregate(0UL, (sum, taken) => sum + taken.Duration);

    public Fmp4Run Before(int sample)
        => this with { Samples = [.. Samples.Take(sample)], Data = Data[..BytesBefore(sample)] };

    public Fmp4Run From(int sample)
        => this with
        {
            DecodeTime = DecodeTime + TicksBefore(sample),
            Samples = [.. Samples.Skip(sample)],
            Data = Data[BytesBefore(sample)..],
        };

    private int BytesBefore(int sample) => (int)Samples.Take(sample).Aggregate(0L, (sum, taken) => sum + taken.Size);
}

/// <summary>
/// A movie fragment read whole, each track's samples and bytes laid out, so that it can be written again or cut in
/// two at a sample. Only a fragment whose track fragments count their data from the movie fragment box is read,
/// which is how the live transcoder writes them.
/// </summary>
public sealed class Fmp4Fragment
{
    private const uint BaseDataOffsetGiven = 0x000001;

    private const uint DescriptionIndexGiven = 0x000002;

    private const uint DefaultDurationGiven = 0x000008;

    private const uint DefaultSizeGiven = 0x000010;

    private const uint DefaultFlagsGiven = 0x000020;

    private const uint DefaultBaseIsTheMovieFragment = 0x020000;

    private const uint DataOffsetGiven = 0x000001;

    private const uint FirstFlagsGiven = 0x000004;

    private const uint DurationGiven = 0x000100;

    private const uint SizeGiven = 0x000200;

    private const uint FlagsGiven = 0x000400;

    private const uint CompositionOffsetGiven = 0x000800;

    private const uint EveryFieldOfASample = DurationGiven | SizeGiven | FlagsGiven | CompositionOffsetGiven;

    private const int HeaderLength = 8;

    private Fmp4Fragment(uint sequence, IReadOnlyList<Fmp4Run> runs)
    {
        Sequence = sequence;
        Runs = runs;
    }

    public uint Sequence { get; }

    public IReadOnlyList<Fmp4Run> Runs { get; }

    public static Fmp4Fragment? Read(ReadOnlyMemory<byte> bytes, IReadOnlyDictionary<uint, Fmp4TrackDefaults> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        if (MovieFragment(bytes.Span) is not { } moof)
        {
            return null;
        }

        ReadOnlyMemory<byte> fromTheMovieFragment = bytes[moof.Start..];
        ReadOnlySpan<byte> body = fromTheMovieFragment.Span.Slice(HeaderLength, moof.Length - HeaderLength);
        uint sequence = Word(IsoBoxWalk.Inside(body, "mfhd"u8), 4) ?? 0U;
        List<Fmp4Run> runs = [];
        var walk = new IsoBoxWalk(body);

        while (walk.Next())
        {
            if (!walk.Named("traf"u8))
            {
                continue;
            }

            if (TrackFragment(walk.Body, fromTheMovieFragment, tracks) is not { } run)
            {
                return null;
            }

            runs.Add(run);
        }

        return new Fmp4Fragment(sequence, runs);
    }

    public Fmp4Run? Of(uint track) => Runs.FirstOrDefault(run => run.Track == track);

    /// <summary>
    /// The fragment cut in two before sample <paramref name="sample"/> of <paramref name="track"/>. Every other track
    /// is cut where its samples begin to be decoded at or after that sample, read on each track's own clock.
    /// </summary>
    public (Fmp4Fragment Before, Fmp4Fragment From) CutAt(uint track, int sample, IReadOnlyDictionary<uint, Fmp4TrackDefaults> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        Fmp4Run cut = Of(track) ?? throw new ArgumentException("A fragment is cut on a track it carries.", nameof(track));
        ulong at = cut.DecodeTime + cut.TicksBefore(sample);
        uint timescale = tracks[track].Timescale;
        List<Fmp4Run> before = [];
        List<Fmp4Run> from = [];

        foreach (Fmp4Run run in Runs)
        {
            int where = run.Track == track ? sample : Where(run, at, timescale, tracks);

            before.Add(run.Before(where));
            from.Add(run.From(where));
        }

        return (new Fmp4Fragment(Sequence, [.. before.Where(run => run.Samples.Count > 0)]),
            new Fmp4Fragment(Sequence, [.. from.Where(run => run.Samples.Count > 0)]));
    }

    /// <summary>
    /// The fragment as a movie fragment box and its media, every field of every sample written out.
    /// </summary>
    public byte[] Written()
    {
        int moofLength = HeaderLength + 16 + Runs.Sum(TrackFragmentLength);
        byte[] written = new byte[moofLength + HeaderLength + Runs.Sum(run => run.Data.Length)];
        int at = 0;
        int data = moofLength + HeaderLength;

        at = Header(written, at, moofLength, "moof"u8);
        at = Header(written, at, 16, "mfhd"u8);
        at = Put(written, at, 0U);
        at = Put(written, at, Sequence);

        foreach (Fmp4Run run in Runs)
        {
            at = TrackFragment(written, at, run, data);
            data += run.Data.Length;
        }

        at = Header(written, at, written.Length - moofLength, "mdat"u8);

        foreach (Fmp4Run run in Runs)
        {
            run.Data.Span.CopyTo(written.AsSpan(at));
            at += run.Data.Length;
        }

        return written;
    }

    private static int Where(Fmp4Run run, ulong at, uint timescale, IReadOnlyDictionary<uint, Fmp4TrackDefaults> tracks)
    {
        if (!tracks.TryGetValue(run.Track, out Fmp4TrackDefaults? defaults) || defaults.Timescale is 0U)
        {
            return run.Samples.Count;
        }

        UInt128 limit = (UInt128)at * defaults.Timescale;
        ulong decoded = run.DecodeTime;
        int where = 0;

        while (where < run.Samples.Count && (UInt128)decoded * timescale < limit)
        {
            decoded += run.Samples[where].Duration;
            where++;
        }

        return where;
    }

    private static (int Start, int Length)? MovieFragment(ReadOnlySpan<byte> bytes)
    {
        int start = 0;

        while (start + HeaderLength <= bytes.Length)
        {
            long length = BoxLength(bytes, start);

            if (length < HeaderLength || length > bytes.Length - start)
            {
                return null;
            }

            if (bytes.Slice(start + 4, 4).SequenceEqual("moof"u8))
            {
                return (start, (int)length);
            }

            start += (int)length;
        }

        return null;
    }

    private static long BoxLength(ReadOnlySpan<byte> bytes, int at)
    {
        uint said = BinaryPrimitives.ReadUInt32BigEndian(bytes[at..]);

        return said switch
        {
            0 => bytes.Length - at,
            1 when at + 16 <= bytes.Length => (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(bytes[(at + HeaderLength)..]), (ulong)long.MaxValue),
            1 => 0,
            _ => said,
        };
    }

    private static uint? Word(ReadOnlySpan<byte> box, int at)
        => at >= 0 && at + 4 <= box.Length ? BinaryPrimitives.ReadUInt32BigEndian(box[at..]) : null;

    private static uint FlagsOf(ReadOnlySpan<byte> box) => (Word(box, 0) ?? 0U) & 0x00FFFFFF;

    private static bool Given(uint flags, uint flag) => (flags & flag) is not 0U;

    private static Fmp4Run? TrackFragment(
        ReadOnlySpan<byte> traf,
        ReadOnlyMemory<byte> fromTheMovieFragment,
        IReadOnlyDictionary<uint, Fmp4TrackDefaults> tracks)
    {
        ReadOnlySpan<byte> tfhd = IsoBoxWalk.Inside(traf, "tfhd"u8);
        uint flags = FlagsOf(tfhd);

        if (Word(tfhd, 4) is not { } track
            || !tracks.TryGetValue(track, out Fmp4TrackDefaults? declared)
            || Given(flags, BaseDataOffsetGiven)
            || !Given(flags, DefaultBaseIsTheMovieFragment)
            || FragmentDefaults(tfhd, flags, declared) is not { } defaults
            || DecodeTime(IsoBoxWalk.Inside(traf, "tfdt"u8)) is not { } decoded)
        {
            return null;
        }

        List<Fmp4Sample> samples = [];
        List<ReadOnlyMemory<byte>> data = [];
        bool signed = false;
        var walk = new IsoBoxWalk(traf);

        while (walk.Next())
        {
            if (!walk.Named("trun"u8))
            {
                continue;
            }

            if (!TrackRun(walk.Body, defaults, fromTheMovieFragment, samples, data))
            {
                return null;
            }

            signed |= walk.Body[0] is 1;
        }

        return new Fmp4Run(track, decoded, samples, Joined(data), signed);
    }

    private static ReadOnlyMemory<byte> Joined(List<ReadOnlyMemory<byte>> parts)
    {
        if (parts.Count is 1)
        {
            return parts[0];
        }

        byte[] joined = new byte[parts.Sum(part => part.Length)];
        int at = 0;

        foreach (ReadOnlyMemory<byte> part in parts)
        {
            part.Span.CopyTo(joined.AsSpan(at));
            at += part.Length;
        }

        return joined;
    }

    private static ulong? DecodeTime(ReadOnlySpan<byte> tfdt)
    {
        if (tfdt.Length < 8)
        {
            return null;
        }

        return tfdt[0] is 1
            ? (tfdt.Length < 12 ? null : BinaryPrimitives.ReadUInt64BigEndian(tfdt[4..]))
            : BinaryPrimitives.ReadUInt32BigEndian(tfdt[4..]);
    }

    private static Fmp4TrackDefaults? FragmentDefaults(ReadOnlySpan<byte> tfhd, uint flags, Fmp4TrackDefaults track)
    {
        int at = 8 + (Given(flags, DescriptionIndexGiven) ? 4 : 0);
        uint? duration = Given(flags, DefaultDurationGiven) ? Word(tfhd, at) : track.Duration;

        at += Given(flags, DefaultDurationGiven) ? 4 : 0;

        uint? size = Given(flags, DefaultSizeGiven) ? Word(tfhd, at) : track.Size;

        at += Given(flags, DefaultSizeGiven) ? 4 : 0;

        uint? sampleFlags = Given(flags, DefaultFlagsGiven) ? Word(tfhd, at) : track.Flags;

        return duration is { } lasting && size is { } bytes && sampleFlags is { } said
            ? track with { Duration = lasting, Size = bytes, Flags = said }
            : null;
    }

    private static bool TrackRun(
        ReadOnlySpan<byte> trun,
        Fmp4TrackDefaults defaults,
        ReadOnlyMemory<byte> fromTheMovieFragment,
        List<Fmp4Sample> samples,
        List<ReadOnlyMemory<byte>> data)
    {
        uint flags = FlagsOf(trun);
        int at = 8;

        if (Word(trun, 4) is not { } count || !Given(flags, DataOffsetGiven) || Word(trun, at) is not { } offset)
        {
            return false;
        }

        at += 4;

        uint? first = Given(flags, FirstFlagsGiven) ? Word(trun, at) : null;

        at += Given(flags, FirstFlagsGiven) ? 4 : 0;

        int stride = 4 * BitOperations.PopCount(flags & EveryFieldOfASample);

        if ((Given(flags, FirstFlagsGiven) && first is null) || (long)at + ((long)count * stride) > trun.Length)
        {
            return false;
        }

        long bytes = 0;

        for (int sample = 0; sample < count; sample++)
        {
            Fmp4Sample read = Sample(trun.Slice(at + (sample * stride), stride), flags, defaults);

            samples.Add(sample is 0 && first is { } said ? read with { Flags = said } : read);
            bytes += read.Size;
        }

        if ((int)offset < 0 || (long)(int)offset + bytes > fromTheMovieFragment.Length)
        {
            return false;
        }

        data.Add(fromTheMovieFragment.Slice((int)offset, (int)bytes));

        return true;
    }

    private static Fmp4Sample Sample(ReadOnlySpan<byte> fields, uint flags, Fmp4TrackDefaults defaults)
    {
        int at = 0;
        uint duration = Field(fields, flags, DurationGiven, ref at) ?? defaults.Duration;
        uint size = Field(fields, flags, SizeGiven, ref at) ?? defaults.Size;
        uint sampleFlags = Field(fields, flags, FlagsGiven, ref at) ?? defaults.Flags;
        uint offset = Field(fields, flags, CompositionOffsetGiven, ref at) ?? 0U;

        return new Fmp4Sample(duration, size, sampleFlags, offset);
    }

    private static uint? Field(ReadOnlySpan<byte> fields, uint flags, uint field, ref int at)
    {
        if (!Given(flags, field))
        {
            return null;
        }

        uint read = BinaryPrimitives.ReadUInt32BigEndian(fields[at..]);

        at += 4;

        return read;
    }

    private static int TrackFragmentLength(Fmp4Run run)
        => HeaderLength + (HeaderLength + 8) + (HeaderLength + 12) + (HeaderLength + 12 + (run.Samples.Count * 16));

    private static int Header(byte[] into, int at, int length, ReadOnlySpan<byte> name)
    {
        BinaryPrimitives.WriteUInt32BigEndian(into.AsSpan(at), (uint)length);
        name.CopyTo(into.AsSpan(at + 4));

        return at + HeaderLength;
    }

    private static int Put(byte[] into, int at, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(into.AsSpan(at), value);

        return at + 4;
    }

    private static int TrackFragment(byte[] into, int at, Fmp4Run run, int data)
    {
        int written = Header(into, at, TrackFragmentLength(run), "traf"u8);

        written = Header(into, written, HeaderLength + 8, "tfhd"u8);
        written = Put(into, written, DefaultBaseIsTheMovieFragment);
        written = Put(into, written, run.Track);
        written = Header(into, written, HeaderLength + 12, "tfdt"u8);
        written = Put(into, written, 0x01000000);
        BinaryPrimitives.WriteUInt64BigEndian(into.AsSpan(written), run.DecodeTime);
        written += 8;
        written = Header(into, written, HeaderLength + 12 + (run.Samples.Count * 16), "trun"u8);
        written = Put(into, written, (run.SignedOffsets ? 0x01000000U : 0U) | DataOffsetGiven | EveryFieldOfASample);
        written = Put(into, written, (uint)run.Samples.Count);
        written = Put(into, written, (uint)data);

        foreach (Fmp4Sample sample in run.Samples)
        {
            written = Put(into, written, sample.Duration);
            written = Put(into, written, sample.Size);
            written = Put(into, written, sample.Flags);
            written = Put(into, written, sample.CompositionOffset);
        }

        return written;
    }
}
