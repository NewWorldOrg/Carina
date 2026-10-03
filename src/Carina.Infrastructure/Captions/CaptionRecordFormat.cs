using System.Buffers.Binary;

using Carina.Domain.Captions;

namespace Carina.Infrastructure.Captions;

/// <summary>
/// How a <see cref="CaptionRecord"/> is laid out on disk: a header naming the format, the canvas and where
/// the file's clock begins, then each change as its 90 kHz moment, its placement and its palette PNG, with
/// an empty placement and no PNG for a screen cleared. Every number is big-endian.
/// </summary>
public static class CaptionRecordFormat
{
    public const byte Version = 1;

    public const int HeaderLength = 8 + 1 + 2 + 2 + 8 + 4;

    public const int CueHeaderLength = 8 + 2 + 2 + 2 + 2 + 4;

    private static readonly byte[] Magic = "CARINACC"u8.ToArray();

    public static byte[] Written(CaptionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        int length = HeaderLength + record.Cues.Sum(cue => CueHeaderLength + (cue.Picture?.Png.Length ?? 0));
        byte[] written = new byte[length];
        Span<byte> at = written;

        Magic.CopyTo(at);
        at[Magic.Length] = Version;
        at = at[(Magic.Length + 1)..];
        BinaryPrimitives.WriteUInt16BigEndian(at, (ushort)record.Width);
        BinaryPrimitives.WriteUInt16BigEndian(at[2..], (ushort)record.Height);
        BinaryPrimitives.WriteInt64BigEndian(at[4..], record.StartsAt.Ticks);
        BinaryPrimitives.WriteInt32BigEndian(at[12..], record.Cues.Count);
        at = at[16..];

        foreach (CaptionCue cue in record.Cues)
        {
            at = Cue(at, cue);
        }

        return written;
    }

    /// <summary>
    /// Reads a record back, or answers null when the bytes are not one this format wrote.
    /// </summary>
    public static CaptionRecord? Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderLength || !bytes[..Magic.Length].SequenceEqual(Magic) || bytes[Magic.Length] != Version)
        {
            return null;
        }

        ReadOnlySpan<byte> at = bytes[(Magic.Length + 1)..];
        int width = BinaryPrimitives.ReadUInt16BigEndian(at);
        int height = BinaryPrimitives.ReadUInt16BigEndian(at[2..]);
        long startsAt = BinaryPrimitives.ReadInt64BigEndian(at[4..]);
        int count = BinaryPrimitives.ReadInt32BigEndian(at[12..]);

        if (width < 1 || height < 1 || count < 0)
        {
            return null;
        }

        at = at[16..];
        List<CaptionCue> cues = new(Math.Min(count, at.Length / CueHeaderLength));

        for (int read = 0; read < count; read++)
        {
            if (Cue(ref at) is not { } cue)
            {
                return null;
            }

            cues.Add(cue);
        }

        return at.IsEmpty ? new CaptionRecord(width, height, TimeSpan.FromTicks(startsAt), cues) : null;
    }

    private static Span<byte> Cue(Span<byte> at, CaptionCue cue)
    {
        BinaryPrimitives.WriteInt64BigEndian(at, cue.Pts);

        if (cue.Picture is { } picture)
        {
            BinaryPrimitives.WriteUInt16BigEndian(at[8..], (ushort)picture.Left);
            BinaryPrimitives.WriteUInt16BigEndian(at[10..], (ushort)picture.Top);
            BinaryPrimitives.WriteUInt16BigEndian(at[12..], (ushort)picture.Width);
            BinaryPrimitives.WriteUInt16BigEndian(at[14..], (ushort)picture.Height);
            BinaryPrimitives.WriteInt32BigEndian(at[16..], picture.Png.Length);
            picture.Png.Span.CopyTo(at[CueHeaderLength..]);
        }

        return at[(CueHeaderLength + (cue.Picture?.Png.Length ?? 0))..];
    }

    private static CaptionCue? Cue(ref ReadOnlySpan<byte> at)
    {
        if (at.Length < CueHeaderLength)
        {
            return null;
        }

        long pts = BinaryPrimitives.ReadInt64BigEndian(at);
        int left = BinaryPrimitives.ReadUInt16BigEndian(at[8..]);
        int top = BinaryPrimitives.ReadUInt16BigEndian(at[10..]);
        int width = BinaryPrimitives.ReadUInt16BigEndian(at[12..]);
        int height = BinaryPrimitives.ReadUInt16BigEndian(at[14..]);
        int length = BinaryPrimitives.ReadInt32BigEndian(at[16..]);

        if (length < 0 || at.Length - CueHeaderLength < length)
        {
            return null;
        }

        ReadOnlySpan<byte> png = at.Slice(CueHeaderLength, length);

        at = at[(CueHeaderLength + length)..];

        if (length is 0)
        {
            return left is 0 && top is 0 && width is 0 && height is 0 ? new CaptionCue(pts, null) : null;
        }

        return width is 0 || height is 0
            ? null
            : new CaptionCue(pts, new CaptionPlacement(left, top, width, height, png.ToArray()));
    }
}
