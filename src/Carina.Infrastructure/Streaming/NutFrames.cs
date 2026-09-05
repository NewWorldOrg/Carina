using System.Buffers.Binary;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public enum NutFault
{
    NotTheContainerItWasAskedFor = 1,

    AHeaderThatCannotBeRead = 2,

    AFrameCodeNobodyDefined = 3,

    AFrameTooBigToHold = 4,

    StoppedPartWayThroughAFrame = 5,
}

public sealed record NutFrame(LivePts Pts, ReadOnlyMemory<byte> Data);

public sealed record NutReading(IReadOnlyList<NutFrame> Frames, NutFault? Fault)
{
    public static readonly NutReading Nothing = new([], null);
}

public sealed class NutFrames
{
    public const int LargestFrame = 16 * 1024 * 1024;

    private const int LargestHeader = 1024 * 1024;

    private const int StartcodeLength = 8;

    private const int ChecksumLength = 4;

    private const byte StartcodeLead = (byte)'N';

    private const ulong MainStartcode = 0x7A561F5F04ADUL + ((((ulong)'N' << 8) + 'M') << 48);

    private const ulong StreamStartcode = 0x11405BF2F9DBUL + ((((ulong)'N' << 8) + 'S') << 48);

    private const ulong SyncpointStartcode = 0xE4ADEECA4569UL + ((((ulong)'N' << 8) + 'K') << 48);

    private const int FlagCodedPts = 8;

    private const int FlagStreamId = 16;

    private const int FlagSizeMsb = 32;

    private const int FlagChecksum = 64;

    private const int FlagReserved = 128;

    private const int FlagSideData = 256;

    private const int FlagHeaderIdx = 1024;

    private const int FlagMatchTime = 2048;

    private const int FlagCoded = 4096;

    private const int FlagInvalid = 8192;

    private static readonly byte[] Magic = "nut/multimedia container\0"u8.ToArray();

    private static readonly IReadOnlyList<NutFrame> None = [];

    private readonly FrameCode[] codes = new FrameCode[256];

    private readonly List<(long Numerator, long Denominator)> timeBases = [];

    private readonly List<byte[]> elisionHeaders = [];

    private readonly Dictionary<int, NutStream> streams = [];

    private readonly List<byte> held = [];

    private bool begun;

    private NutFault? fault;

    public NutFrames()
    {
        for (int code = 0; code < codes.Length; code++)
        {
            codes[code] = FrameCode.Invalid;
        }
    }

    public NutFault? Fault => fault;

    public NutReading Read(ReadOnlySpan<byte> bytes)
    {
        if (fault is { } already)
        {
            return new NutReading(None, already);
        }

        held.AddRange(bytes);

        List<NutFrame> frames = [];
        int at = 0;

        while (fault is null)
        {
            int consumed = Element(held.ToArray().AsSpan(at), frames);

            if (consumed is 0)
            {
                break;
            }

            at += consumed;
        }

        held.RemoveRange(0, at);

        return new NutReading(frames, fault);
    }

    public NutReading Ended()
    {
        if (fault is { } already)
        {
            return new NutReading(None, already);
        }

        if (held.Count > 0)
        {
            fault = NutFault.StoppedPartWayThroughAFrame;
        }

        return new NutReading(None, fault);
    }

    private int Element(ReadOnlySpan<byte> bytes, List<NutFrame> frames)
    {
        if (!begun)
        {
            if (bytes.Length < Magic.Length)
            {
                return Broken(bytes, NutFault.NotTheContainerItWasAskedFor, whenLongerThan: Magic.Length - 1);
            }

            if (!bytes[..Magic.Length].SequenceEqual(Magic))
            {
                fault = NutFault.NotTheContainerItWasAskedFor;

                return 0;
            }

            begun = true;

            return Magic.Length;
        }

        if (bytes.IsEmpty)
        {
            return 0;
        }

        return bytes[0] == StartcodeLead ? Packet(bytes) : Frame(bytes, frames);
    }

    private int Broken(ReadOnlySpan<byte> bytes, NutFault when, int whenLongerThan)
    {
        if (bytes.Length > whenLongerThan)
        {
            fault = when;
        }

        return 0;
    }

    private int Packet(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < StartcodeLength)
        {
            return 0;
        }

        ulong startcode = BinaryPrimitives.ReadUInt64BigEndian(bytes);
        Cursor cursor = new(bytes[StartcodeLength..]);

        if (!cursor.TryUnsigned(out long forward))
        {
            return 0;
        }

        if (forward > LargestHeader || forward < ChecksumLength)
        {
            fault = NutFault.AHeaderThatCannotBeRead;

            return 0;
        }

        if (forward > 4096 && !cursor.TrySkip(ChecksumLength))
        {
            return 0;
        }

        if (cursor.Remaining < forward)
        {
            return 0;
        }

        ReadOnlySpan<byte> body = cursor.Take((int)forward)[..^ChecksumLength];
        bool read = startcode switch
        {
            MainStartcode => MainHeader(body),
            StreamStartcode => StreamHeader(body),
            SyncpointStartcode => Syncpoint(body),
            _ => true,
        };

        if (!read)
        {
            fault = NutFault.AHeaderThatCannotBeRead;

            return 0;
        }

        return StartcodeLength + cursor.Consumed;
    }

    private bool MainHeader(ReadOnlySpan<byte> body)
    {
        Cursor cursor = new(body);

        if (!cursor.TryUnsigned(out long version) || version < 2 || version > 4)
        {
            return false;
        }

        if (version > 3 && !cursor.TryUnsigned(out _))
        {
            return false;
        }

        if (!cursor.TryUnsigned(out long streamCount) || streamCount < 1
            || !cursor.TryUnsigned(out _)
            || !cursor.TryUnsigned(out long timeBaseCount) || timeBaseCount < 1 || timeBaseCount > body.Length)
        {
            return false;
        }

        timeBases.Clear();

        for (long clock = 0; clock < timeBaseCount; clock++)
        {
            if (!cursor.TryUnsigned(out long numerator) || !cursor.TryUnsigned(out long denominator)
                || numerator < 1 || denominator < 1)
            {
                return false;
            }

            timeBases.Add((numerator, denominator));
        }

        long pts = 0;
        long multiplier = 1;
        long stream = 0;
        long headerIndex = 0;

        for (int code = 0; code < codes.Length;)
        {
            if (!cursor.TryUnsigned(out long flags) || !cursor.TryUnsigned(out long fields))
            {
                return false;
            }

            if (fields > 0 && !cursor.TrySigned(out pts))
            {
                return false;
            }

            if (fields > 1 && !cursor.TryUnsigned(out multiplier))
            {
                return false;
            }

            if (fields > 2 && !cursor.TryUnsigned(out stream))
            {
                return false;
            }

            long size = 0;

            if (fields > 3 && !cursor.TryUnsigned(out size))
            {
                return false;
            }

            long reserved = 0;

            if (fields > 4 && !cursor.TryUnsigned(out reserved))
            {
                return false;
            }

            long count = multiplier - size;

            if (fields > 5 && !cursor.TryUnsigned(out count))
            {
                return false;
            }

            if (fields > 6 && !cursor.TrySigned(out _))
            {
                return false;
            }

            if (fields > 7 && !cursor.TryUnsigned(out headerIndex))
            {
                return false;
            }

            for (long field = 8; field < fields; field++)
            {
                if (!cursor.TryUnsigned(out _))
                {
                    return false;
                }
            }

            if (count < 1 || count > codes.Length - (code <= StartcodeLead ? 1 : 0) - code || stream >= streamCount)
            {
                return false;
            }

            for (long taken = 0; taken < count; taken++, code++)
            {
                if (code == StartcodeLead)
                {
                    codes[code] = FrameCode.Invalid;
                    taken--;

                    continue;
                }

                codes[code] = new FrameCode((int)flags, (int)stream, multiplier, size + taken, pts, (int)reserved, (int)headerIndex);
            }
        }

        elisionHeaders.Clear();
        elisionHeaders.Add([]);

        if (cursor.Remaining > 0)
        {
            if (!cursor.TryUnsigned(out long headerCount) || headerCount >= 128)
            {
                return false;
            }

            for (long header = 0; header < headerCount; header++)
            {
                if (!cursor.TryUnsigned(out long length) || length < 1 || length > 255 || cursor.Remaining < length)
                {
                    return false;
                }

                elisionHeaders.Add(cursor.Take((int)length).ToArray());
            }
        }

        return true;
    }

    private bool StreamHeader(ReadOnlySpan<byte> body)
    {
        Cursor cursor = new(body);

        if (!cursor.TryUnsigned(out long stream) || stream > int.MaxValue
            || !cursor.TryUnsigned(out _)
            || !cursor.TryUnsigned(out long tag) || cursor.Remaining < tag || !cursor.TrySkip((int)tag)
            || !cursor.TryUnsigned(out long clock) || clock >= timeBases.Count
            || !cursor.TryUnsigned(out long shift) || shift >= 16)
        {
            return false;
        }

        streams[(int)stream] = new NutStream((int)clock, (int)shift);

        return true;
    }

    private bool Syncpoint(ReadOnlySpan<byte> body)
    {
        Cursor cursor = new(body);

        if (timeBases.Count is 0 || !cursor.TryUnsigned(out long stamped) || !cursor.TryUnsigned(out _))
        {
            return false;
        }

        long ticks = stamped / timeBases.Count;
        (long numerator, long denominator) = timeBases[(int)(stamped % timeBases.Count)];

        foreach (NutStream stream in streams.Values)
        {
            (long ownNumerator, long ownDenominator) = timeBases[stream.Clock];

            stream.LastPts = (long)((UInt128)ticks * (ulong)numerator * (ulong)ownDenominator / ((ulong)denominator * (ulong)ownNumerator));
        }

        return true;
    }

    private int Frame(ReadOnlySpan<byte> bytes, List<NutFrame> frames)
    {
        FrameCode code = codes[bytes[0]];

        if ((code.Flags & FlagInvalid) is not 0)
        {
            fault = NutFault.AFrameCodeNobodyDefined;

            return 0;
        }

        Cursor cursor = new(bytes[1..]);
        long flags = code.Flags;

        if ((flags & FlagCoded) is not 0)
        {
            if (!cursor.TryUnsigned(out long coded))
            {
                return 0;
            }

            flags ^= coded;
        }

        long streamId = code.Stream;

        if ((flags & FlagStreamId) is not 0 && !cursor.TryUnsigned(out streamId))
        {
            return 0;
        }

        if (!streams.TryGetValue((int)streamId, out NutStream? stream))
        {
            fault = NutFault.AHeaderThatCannotBeRead;

            return 0;
        }

        long pts = stream.LastPts + code.PtsDelta;

        if ((flags & FlagCodedPts) is not 0)
        {
            if (!cursor.TryUnsigned(out long coded))
            {
                return 0;
            }

            long span = 1L << stream.Shift;

            if (coded < span)
            {
                long mask = span - 1;
                long delta = stream.LastPts - (mask / 2);

                pts = ((coded - delta) & mask) + delta;
            }
            else
            {
                pts = coded - span;
            }
        }

        long size = code.SizeLsb;

        if ((flags & FlagSizeMsb) is not 0)
        {
            if (!cursor.TryUnsigned(out long most))
            {
                return 0;
            }

            size += code.SizeMultiplier * most;
        }

        if ((flags & FlagMatchTime) is not 0 && !cursor.TrySigned(out _))
        {
            return 0;
        }

        long headerIndex = code.HeaderIndex;

        if ((flags & FlagHeaderIdx) is not 0 && !cursor.TryUnsigned(out headerIndex))
        {
            return 0;
        }

        long reserved = code.Reserved;

        if ((flags & FlagReserved) is not 0 && !cursor.TryUnsigned(out reserved))
        {
            return 0;
        }

        for (long field = 0; field < reserved; field++)
        {
            if (!cursor.TryUnsigned(out _))
            {
                return 0;
            }
        }

        if ((flags & FlagChecksum) is not 0 && !cursor.TrySkip(ChecksumLength))
        {
            return 0;
        }

        if ((flags & FlagSideData) is not 0 || headerIndex >= elisionHeaders.Count)
        {
            fault = NutFault.AHeaderThatCannotBeRead;

            return 0;
        }

        if (size > LargestFrame)
        {
            fault = NutFault.AFrameTooBigToHold;

            return 0;
        }

        byte[] elided = size > 4096 ? [] : elisionHeaders[(int)headerIndex];
        long body = size - elided.Length;

        if (body < 0)
        {
            fault = NutFault.AHeaderThatCannotBeRead;

            return 0;
        }

        if (cursor.Remaining < body)
        {
            return 0;
        }

        byte[] data = new byte[size];

        elided.CopyTo(data, 0);
        cursor.Take((int)body).CopyTo(data.AsSpan(elided.Length));

        stream.LastPts = pts;
        (long clockNumerator, long clockDenominator) = timeBases[stream.Clock];

        frames.Add(new NutFrame(Stamped(pts, clockNumerator, clockDenominator), data));

        return 1 + cursor.Consumed;
    }

    private static LivePts Stamped(long pts, long numerator, long denominator)
        => pts <= 0 ? LivePts.Start : LivePts.Rescaled((ulong)pts * (ulong)numerator, (uint)Math.Min(denominator, uint.MaxValue));

    private readonly record struct FrameCode(
        int Flags,
        int Stream,
        long SizeMultiplier,
        long SizeLsb,
        long PtsDelta,
        int Reserved,
        int HeaderIndex)
    {
        public static readonly FrameCode Invalid = new(FlagInvalid, 0, 1, 0, 0, 0, 0);
    }

    private sealed class NutStream(int clock, int shift)
    {
        public int Clock { get; } = clock;

        public int Shift { get; } = shift;

        public long LastPts { get; set; }
    }

    private ref struct Cursor(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> bytes = bytes;

        public int Consumed { get; private set; }

        public readonly int Remaining => bytes.Length - Consumed;

        public bool TryUnsigned(out long value)
        {
            value = 0;
            int at = Consumed;

            while (at < bytes.Length)
            {
                byte one = bytes[at++];

                if (at - Consumed > 10)
                {
                    return false;
                }

                value = (value << 7) | (long)(one & 0x7F);

                if ((one & 0x80) is 0)
                {
                    Consumed = at;

                    return true;
                }
            }

            return false;
        }

        public bool TrySigned(out long value)
        {
            if (!TryUnsigned(out long unsigned))
            {
                value = 0;

                return false;
            }

            long shifted = unsigned + 1;

            value = (shifted & 1) is 1 ? -(shifted >> 1) : shifted >> 1;

            return true;
        }

        public bool TrySkip(int count)
        {
            if (Remaining < count)
            {
                return false;
            }

            Consumed += count;

            return true;
        }

        public ReadOnlySpan<byte> Take(int count)
        {
            ReadOnlySpan<byte> taken = bytes.Slice(Consumed, count);

            Consumed += count;

            return taken;
        }
    }
}
