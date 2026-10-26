using System.Buffers.Binary;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// Walks the boxes lying side by side in an ISO base media file, one level deep, stopping at the first box whose size
/// does not fit what is left.
/// </summary>
internal ref struct IsoBoxWalk(ReadOnlySpan<byte> boxes)
{
    private const int HeaderLength = 8;

    private const int WideHeaderLength = 16;

    private readonly ReadOnlySpan<byte> boxes = boxes;

    private int at;

    public ReadOnlySpan<byte> Name { get; private set; }

    public ReadOnlySpan<byte> Body { get; private set; }

    public static ReadOnlySpan<byte> Inside(ReadOnlySpan<byte> boxes, ReadOnlySpan<byte> named)
    {
        var walk = new IsoBoxWalk(boxes);

        while (walk.Next())
        {
            if (walk.Named(named))
            {
                return walk.Body;
            }
        }

        return [];
    }

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
