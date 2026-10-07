using System.Buffers.Binary;
using System.Text;

namespace Carina.Infrastructure.Segments;

public enum MatroskaTrackKind
{
    Other = 0,

    Video = 1,

    Audio = 2,
}

/// <summary>
/// A track as the header of a Matroska stream describes it. A value the header leaves out is null.
/// </summary>
public sealed record MatroskaTrack(
    int Number,
    MatroskaTrackKind Kind,
    string Codec,
    TimeSpan? FrameLength,
    int? Width,
    int? Height,
    double? SampleRate,
    int? Channels,
    int? BitDepth);

/// <summary>
/// What a <see cref="MatroskaReader"/> hands on: the tracks once their header is read, and every block
/// with the number of its track and its time.
/// </summary>
public interface IMatroskaSink
{
    void Tracks(IReadOnlyList<MatroskaTrack> tracks);

    void Block(int track, TimeSpan at, ReadOnlySpan<byte> payload);
}

/// <summary>
/// Reads the Matroska ffmpeg writes to a pipe, as far as the learning data needs it: the EBML header,
/// a segment of known or unknown size, its information and its tracks, and its clusters of known or
/// unknown size with their time and their blocks, simple or in a group, none of them laced. Any other
/// element is passed over. Bytes are handed over in pieces of any length; a piece that ends inside an
/// element the reader needs whole is read up to that element, which is read once the rest has come.
/// </summary>
public sealed class MatroskaReader(IMatroskaSink sink)
{
    public const int LargestElement = 64 << 20;

    public const int FirstBuffer = 1 << 18;

    public const long DefaultTimestampScale = 1_000_000;

    private const long Unsized = -1;

    private const int NanosecondsPerTick = 100;

    private const int Laced = 6;

    private const uint EbmlHeader = 0x1A45DFA3;

    private const uint Segment = 0x18538067;

    private const uint Info = 0x1549A966;

    private const uint TimestampScale = 0x2AD7B1;

    private const uint Tracks = 0x1654AE6B;

    private const uint TrackEntry = 0xAE;

    private const uint TrackNumber = 0xD7;

    private const uint TrackType = 0x83;

    private const uint CodecId = 0x86;

    private const uint DefaultDuration = 0x23E383;

    private const uint Video = 0xE0;

    private const uint PixelWidth = 0xB0;

    private const uint PixelHeight = 0xBA;

    private const uint Audio = 0xE1;

    private const uint SamplingFrequency = 0xB5;

    private const uint Channels = 0x9F;

    private const uint BitDepth = 0x6264;

    private const uint Cluster = 0x1F43B675;

    private const uint ClusterTimestamp = 0xE7;

    private const uint ClusterPosition = 0xA7;

    private const uint PreviousSize = 0xAB;

    private const uint SimpleBlock = 0xA3;

    private const uint BlockGroup = 0xA0;

    private const uint Block = 0xA1;

    private const uint Void = 0xEC;

    private const uint Crc = 0xBF;

    private static readonly HashSet<uint> Entered = [Segment, Info, Tracks, TrackEntry, Video, Audio, Cluster, BlockGroup];

    private static readonly HashSet<uint> MayBeUnsized = [Segment, Cluster];

    private static readonly HashSet<uint> InACluster = [ClusterTimestamp, ClusterPosition, PreviousSize, SimpleBlock, BlockGroup, Void, Crc];

    private static readonly HashSet<uint> TakenWhole =
    [
        TimestampScale,
        TrackNumber,
        TrackType,
        CodecId,
        DefaultDuration,
        PixelWidth,
        PixelHeight,
        SamplingFrequency,
        Channels,
        BitDepth,
        ClusterTimestamp,
        SimpleBlock,
        Block,
    ];

    private readonly Stack<(uint Id, long Ends)> open = new();

    private readonly List<MatroskaTrack> tracks = [];

    private TrackHeader? track;

    private long scale = DefaultTimestampScale;

    private long? clusterAt;

    private long position;

    private long passing;

    private bool begun;

    /// <summary>
    /// Reads as many whole elements as <paramref name="bytes"/> holds and says how many bytes that took.
    /// </summary>
    /// <exception cref="InvalidDataException">The bytes are not Matroska this reader can read.</exception>
    public int ReadFrom(ReadOnlySpan<byte> bytes)
    {
        int at = 0;

        while (at < bytes.Length)
        {
            int used = passing > 0 ? PassOver(bytes.Length - at) : Element(bytes[at..]);

            if (used is 0)
            {
                break;
            }

            at += used;
            position += used;
        }

        return at;
    }

    /// <summary>
    /// Reads the stream to its end, waiting on <paramref name="afterEach"/> after each piece that came,
    /// and says whether the stream ended between two elements rather than inside one.
    /// </summary>
    /// <exception cref="InvalidDataException">The bytes are not Matroska this reader can read.</exception>
    public async Task<bool> ReadToEndAsync(Stream stream, Func<CancellationToken, Task> afterEach, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(afterEach);

        byte[] buffer = new byte[FirstBuffer];
        int held = 0;

        while (true)
        {
            if (held == buffer.Length)
            {
                buffer = Larger(buffer);
            }

            int came = await stream.ReadAsync(buffer.AsMemory(held), cancellationToken);

            if (came is 0)
            {
                return held is 0 && passing is 0;
            }

            held += came;

            int used = ReadFrom(buffer.AsSpan(0, held));

            held -= used;
            Array.Copy(buffer, used, buffer, 0, held);

            await afterEach(cancellationToken);
        }
    }

    private static byte[] Larger(byte[] buffer)
    {
        if (buffer.Length > LargestElement)
        {
            throw new InvalidDataException($"No element this reader needs whole is larger than {LargestElement} bytes.");
        }

        byte[] larger = new byte[buffer.Length * 2];
        Array.Copy(buffer, larger, buffer.Length);

        return larger;
    }

    private int PassOver(int available)
    {
        int used = (int)Math.Min(passing, available);
        passing -= used;

        return used;
    }

    private int Element(ReadOnlySpan<byte> rest)
    {
        if (!Header(rest, out uint id, out long size, out int header))
        {
            return 0;
        }

        Close(id);
        Begin(id);

        if (Entered.Contains(id))
        {
            Enter(id, size, header);

            return header;
        }

        if (size is Unsized)
        {
            throw new InvalidDataException($"Element {id:X} gives no size, and only a segment or a cluster may leave it out.");
        }

        if (!TakenWhole.Contains(id))
        {
            passing = size;

            return header;
        }

        if (size > LargestElement)
        {
            throw new InvalidDataException($"Element {id:X} takes {size} bytes, more than {LargestElement}.");
        }

        if (rest.Length - header < size)
        {
            return 0;
        }

        Leaf(id, rest.Slice(header, (int)size));

        return header + (int)size;
    }

    private void Begin(uint id)
    {
        if (begun)
        {
            return;
        }

        if (id != EbmlHeader)
        {
            throw new InvalidDataException("A Matroska stream begins with its EBML header.");
        }

        begun = true;
    }

    private void Enter(uint id, long size, int header)
    {
        if (size is Unsized && !MayBeUnsized.Contains(id))
        {
            throw new InvalidDataException($"Element {id:X} gives no size, and only a segment or a cluster may leave it out.");
        }

        open.Push((id, size is Unsized ? Unsized : position + header + size));

        if (id == TrackEntry)
        {
            track = new TrackHeader();
        }

        if (id == Cluster)
        {
            clusterAt = null;
        }
    }

    private void Close(uint next)
    {
        while (open.TryPeek(out (uint Id, long Ends) top) && IsOver(top, next))
        {
            open.Pop();
            Closed(top.Id);
        }
    }

    private bool IsOver((uint Id, long Ends) element, uint next)
        => element.Ends is Unsized ? !Within(element.Id, next) : position >= element.Ends;

    private static bool Within(uint parent, uint child)
        => parent == Cluster ? InACluster.Contains(child) : child != EbmlHeader && child != Segment;

    private void Closed(uint id)
    {
        if (id == TrackEntry && track is not null)
        {
            tracks.Add(track.Described());
            track = null;
        }

        if (id == Tracks)
        {
            sink.Tracks([.. tracks]);
        }

        if (id == Cluster)
        {
            clusterAt = null;
        }
    }

    private void Leaf(uint id, ReadOnlySpan<byte> value)
    {
        switch (id)
        {
            case TimestampScale:
                scale = (long)Unsigned(value);

                if (scale <= 0)
                {
                    throw new InvalidDataException("A segment's timestamps are counted in a positive number of nanoseconds.");
                }

                break;
            case ClusterTimestamp:
                clusterAt = (long)Unsigned(value);
                break;
            case SimpleBlock or Block:
                Blocked(value);
                break;
            default:
                track?.Take(id, value);
                break;
        }
    }

    private void Blocked(ReadOnlySpan<byte> value)
    {
        if (clusterAt is not { } cluster)
        {
            throw new InvalidDataException("A block comes before the time of its cluster.");
        }

        if (!Vint(value, out long number, out int width) || value.Length < width + 3)
        {
            throw new InvalidDataException("A block is too short to say its track, its time and its flags.");
        }

        short relative = BinaryPrimitives.ReadInt16BigEndian(value[width..]);

        if ((value[width + 2] & Laced) is not 0)
        {
            throw new InvalidDataException("Laced blocks are not read.");
        }

        long nanoseconds = (cluster + relative) * scale;

        sink.Block((int)number, TimeSpan.FromTicks(nanoseconds / NanosecondsPerTick), value[(width + 3)..]);
    }

    private static bool Header(ReadOnlySpan<byte> rest, out uint id, out long size, out int length)
    {
        id = 0;
        size = 0;
        length = 0;

        if (rest.IsEmpty)
        {
            return false;
        }

        int idWidth = Width(rest[0], 4);

        if (rest.Length <= idWidth)
        {
            return false;
        }

        for (int at = 0; at < idWidth; at++)
        {
            id = (id << 8) | rest[at];
        }

        if (!Vint(rest[idWidth..], out size, out int sizeWidth))
        {
            return false;
        }

        length = idWidth + sizeWidth;

        return true;
    }

    private static bool Vint(ReadOnlySpan<byte> bytes, out long value, out int width)
    {
        value = 0;
        width = Width(bytes[0], 8);

        if (bytes.Length < width)
        {
            return false;
        }

        value = bytes[0] & (0xFF >> width);

        for (int at = 1; at < width; at++)
        {
            value = (value << 8) | bytes[at];
        }

        if (value == (1L << (7 * width)) - 1)
        {
            value = Unsized;
        }

        return true;
    }

    private static int Width(byte first, int most)
    {
        for (int width = 1; width <= most; width++)
        {
            if ((first & (0x80 >> (width - 1))) is not 0)
            {
                return width;
            }
        }

        throw new InvalidDataException($"An EBML number takes at most {most} bytes.");
    }

    private static ulong Unsigned(ReadOnlySpan<byte> value)
    {
        if (value.Length > sizeof(ulong))
        {
            throw new InvalidDataException("An unsigned number takes at most eight bytes.");
        }

        ulong read = 0;

        foreach (byte part in value)
        {
            read = (read << 8) | part;
        }

        return read;
    }

    private static double Float(ReadOnlySpan<byte> value)
        => value.Length switch
        {
            0 => 0,
            sizeof(float) => BinaryPrimitives.ReadSingleBigEndian(value),
            sizeof(double) => BinaryPrimitives.ReadDoubleBigEndian(value),
            _ => throw new InvalidDataException("A float takes four or eight bytes."),
        };

    private sealed class TrackHeader
    {
        private int? number;

        private MatroskaTrackKind kind;

        private string codec = string.Empty;

        private TimeSpan? frameLength;

        private int? width;

        private int? height;

        private double? sampleRate;

        private int? channels;

        private int? bitDepth;

        public void Take(uint id, ReadOnlySpan<byte> value)
        {
            switch (id)
            {
                case TrackNumber:
                    number = (int)Unsigned(value);
                    break;
                case TrackType:
                    kind = Kind(Unsigned(value));
                    break;
                case CodecId:
                    codec = Encoding.ASCII.GetString(value).TrimEnd('\0');
                    break;
                case DefaultDuration:
                    frameLength = TimeSpan.FromTicks((long)(Unsigned(value) / NanosecondsPerTick));
                    break;
                case PixelWidth:
                    width = (int)Unsigned(value);
                    break;
                case PixelHeight:
                    height = (int)Unsigned(value);
                    break;
                case SamplingFrequency:
                    sampleRate = Float(value);
                    break;
                case Channels:
                    channels = (int)Unsigned(value);
                    break;
                case BitDepth:
                    bitDepth = (int)Unsigned(value);
                    break;
            }
        }

        public MatroskaTrack Described()
            => number is { } numbered
                ? new MatroskaTrack(numbered, kind, codec, frameLength, width, height, sampleRate, channels, bitDepth)
                : throw new InvalidDataException("A track entry names no track number.");

        private static MatroskaTrackKind Kind(ulong type)
            => type switch
            {
                1 => MatroskaTrackKind.Video,
                2 => MatroskaTrackKind.Audio,
                _ => MatroskaTrackKind.Other,
            };
    }
}
