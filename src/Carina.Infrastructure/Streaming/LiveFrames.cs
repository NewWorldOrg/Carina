using System.Buffers.Binary;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveFrames
{
    private const int DatesInTheOldestVersion = 8;

    private readonly Dictionary<uint, uint> rates = [];

    private LivePts reached = LivePts.Start;

    public int Unstamped { get; private set; }

    public static LiveChannel Channel(LiveTrack track, LiveFragmentKind kind)
        => (track, kind) switch
        {
            (LiveTrack.Picture, LiveFragmentKind.Initialisation) => LiveChannel.PictureHeader,
            (LiveTrack.Picture, LiveFragmentKind.Media) => LiveChannel.Picture,
            (LiveTrack.Sound, LiveFragmentKind.Initialisation) => LiveChannel.SoundHeader,
            (LiveTrack.Sound, LiveFragmentKind.Media) => LiveChannel.Sound,
            _ => throw new ArgumentOutOfRangeException(
                nameof(track),
                track,
                "A fragment belongs to a track and a kind the wire has a channel for."),
        };

    public LiveFrame Of(LiveFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        LiveChannel channel = Channel(fragment.Track, fragment.Kind);

        if (fragment.Kind is LiveFragmentKind.Initialisation)
        {
            LearnTheRates(fragment.Bytes.Span);

            return new LiveFrame(channel, LivePts.Start, fragment.Bytes);
        }

        if (Earliest(fragment.Bytes.Span) is { } stamped)
        {
            reached = stamped;
        }
        else
        {
            Unstamped++;
        }

        return new LiveFrame(channel, reached, fragment.Bytes);
    }

    private static ReadOnlySpan<byte> Inside(ReadOnlySpan<byte> boxes, ReadOnlySpan<byte> named)
    {
        var walk = new BoxWalk(boxes);

        while (walk.Next())
        {
            if (walk.Named(named))
            {
                return walk.Body;
            }
        }

        return [];
    }

    private static uint? Number(ReadOnlySpan<byte> box, int dates)
    {
        if (box.Length < 4)
        {
            return null;
        }

        int at = 4 + (box[0] is 1 ? dates * 2 : dates);

        return at + 4 <= box.Length ? BinaryPrimitives.ReadUInt32BigEndian(box[at..]) : null;
    }

    private void LearnTheRates(ReadOnlySpan<byte> initialisation)
    {
        rates.Clear();

        var walk = new BoxWalk(Inside(initialisation, "moov"u8));

        while (walk.Next())
        {
            if (walk.Named("trak"u8))
            {
                Learn(walk.Body);
            }
        }
    }

    private void Learn(ReadOnlySpan<byte> trak)
    {
        uint? id = Number(Inside(trak, "tkhd"u8), DatesInTheOldestVersion);
        uint? rate = Number(Inside(Inside(trak, "mdia"u8), "mdhd"u8), DatesInTheOldestVersion);

        if (id is not { } track || rate is not { } ticks || ticks is 0U)
        {
            return;
        }

        rates[track] = ticks;
    }

    private LivePts? Earliest(ReadOnlySpan<byte> media)
    {
        var walk = new BoxWalk(Inside(media, "moof"u8));

        LivePts? earliest = null;

        while (walk.Next())
        {
            if (!walk.Named("traf"u8))
            {
                continue;
            }

            if (Stamp(walk.Body) is { } stamped && (earliest is null || stamped.Value < earliest.Value))
            {
                earliest = stamped;
            }
        }

        return earliest;
    }

    private LivePts? Stamp(ReadOnlySpan<byte> traf)
    {
        ReadOnlySpan<byte> tfhd = Inside(traf, "tfhd"u8);
        ReadOnlySpan<byte> tfdt = Inside(traf, "tfdt"u8);

        if (tfhd.Length < 8 || tfdt.Length < 8)
        {
            return null;
        }

        if (!rates.TryGetValue(BinaryPrimitives.ReadUInt32BigEndian(tfhd[4..]), out uint rate))
        {
            return null;
        }

        if (tfdt[0] is not 1)
        {
            return LivePts.Rescaled(BinaryPrimitives.ReadUInt32BigEndian(tfdt[4..]), rate);
        }

        return tfdt.Length < 12 ? null : LivePts.Rescaled(BinaryPrimitives.ReadUInt64BigEndian(tfdt[4..]), rate);
    }

    private ref struct BoxWalk(ReadOnlySpan<byte> boxes)
    {
        private const int HeaderLength = 8;

        private const int WideHeaderLength = 16;

        private readonly ReadOnlySpan<byte> boxes = boxes;

        private int at;

        public ReadOnlySpan<byte> Name { get; private set; }

        public ReadOnlySpan<byte> Body { get; private set; }

        public readonly bool Named(ReadOnlySpan<byte> named) => Name.SequenceEqual(named);

        public bool Next()
        {
            if (at + HeaderLength > boxes.Length)
            {
                return false;
            }

            uint said = BinaryPrimitives.ReadUInt32BigEndian(boxes[at..]);
            int header = said is 1 ? WideHeaderLength : HeaderLength;

            if (said is 1 && at + WideHeaderLength > boxes.Length)
            {
                return false;
            }

            ulong length = said switch
            {
                0 => (ulong)(boxes.Length - at),
                1 => BinaryPrimitives.ReadUInt64BigEndian(boxes[(at + HeaderLength)..]),
                _ => said,
            };

            if (length < (ulong)header || length > (ulong)(boxes.Length - at))
            {
                return false;
            }

            Name = boxes.Slice(at + 4, 4);
            Body = boxes.Slice(at + header, (int)length - header);
            at += (int)length;

            return true;
        }
    }
}
