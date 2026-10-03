using System.Buffers.Binary;
using System.Text;

using Carina.Domain.Captions;

namespace Carina.Infrastructure.Captions;

/// <summary>
/// How a <see cref="CaptionRecord"/> is laid out on disk: a header naming the format, the canvas and where
/// the file's clock begins, then each change as its 90 kHz moment, its placement and its palette PNG, with
/// an empty placement and no PNG for a screen cleared, and then the count of text changes and each as its
/// 90 kHz moment and its UTF-8 text, with no text for a screen cleared. A record with no text is written in
/// <see cref="TextlessVersion"/>, which ends after the pictures. Every number is big-endian.
/// </summary>
public static class CaptionRecordFormat
{
    public const byte Version = 2;

    public const byte TextlessVersion = 1;

    public const int HeaderLength = 8 + 1 + 2 + 2 + 8 + 4;

    public const int CueHeaderLength = 8 + 2 + 2 + 2 + 2 + 4;

    public const int LineHeaderLength = 8 + 4;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly byte[] Magic = "CARINACC"u8.ToArray();

    public static byte[] Written(CaptionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        byte[][]? texts = record.Lines?.Select(line => line.Text is null ? [] : Utf8.GetBytes(line.Text)).ToArray();
        int length = HeaderLength
            + record.Cues.Sum(cue => CueHeaderLength + (cue.Picture?.Png.Length ?? 0))
            + (texts is null ? 0 : 4 + texts.Sum(text => LineHeaderLength + text.Length));
        byte[] written = new byte[length];
        Span<byte> at = written;

        Magic.CopyTo(at);
        at[Magic.Length] = texts is null ? TextlessVersion : Version;
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

        if (record.Lines is { } lines && texts is not null)
        {
            BinaryPrimitives.WriteInt32BigEndian(at, lines.Count);
            at = at[4..];

            for (int index = 0; index < lines.Count; index++)
            {
                at = Line(at, lines[index].Pts, texts[index]);
            }
        }

        return written;
    }

    /// <summary>
    /// Reads a record back, or answers null when the bytes are not one this format wrote.
    /// </summary>
    public static CaptionRecord? Read(ReadOnlySpan<byte> bytes)
    {
        if (VersionOf(bytes) is not { } version)
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

        if (version == TextlessVersion)
        {
            return at.IsEmpty ? new CaptionRecord(width, height, TimeSpan.FromTicks(startsAt), cues) : null;
        }

        return Lines(ref at) is { } lines && at.IsEmpty
            ? new CaptionRecord(width, height, TimeSpan.FromTicks(startsAt), cues, lines)
            : null;
    }

    /// <summary>
    /// Whether the head of a record says the record carries the text of its captions; false for a record kept
    /// before the text was taken and for a head this format did not write.
    /// </summary>
    public static bool CarriesText(ReadOnlySpan<byte> head) => VersionOf(head) == Version;

    /// <summary>
    /// Reads where the file's clock began from the head of a record alone, or answers null when the head is
    /// not one this format wrote.
    /// </summary>
    public static TimeSpan? StartOf(ReadOnlySpan<byte> head)
    {
        if (VersionOf(head) is null)
        {
            return null;
        }

        return TimeSpan.FromTicks(BinaryPrimitives.ReadInt64BigEndian(head[(Magic.Length + 1 + 4)..]));
    }

    private static byte? VersionOf(ReadOnlySpan<byte> head)
    {
        if (head.Length < HeaderLength || !head[..Magic.Length].SequenceEqual(Magic))
        {
            return null;
        }

        return head[Magic.Length] == Version || head[Magic.Length] == TextlessVersion ? head[Magic.Length] : null;
    }

    private static Span<byte> Line(Span<byte> at, long pts, byte[] text)
    {
        BinaryPrimitives.WriteInt64BigEndian(at, pts);
        BinaryPrimitives.WriteInt32BigEndian(at[8..], text.Length);
        text.CopyTo(at[LineHeaderLength..]);

        return at[(LineHeaderLength + text.Length)..];
    }

    private static List<CaptionLine>? Lines(ref ReadOnlySpan<byte> at)
    {
        if (at.Length < 4)
        {
            return null;
        }

        int count = BinaryPrimitives.ReadInt32BigEndian(at);
        at = at[4..];

        if (count < 0)
        {
            return null;
        }

        List<CaptionLine> lines = new(Math.Min(count, at.Length / LineHeaderLength));

        for (int read = 0; read < count; read++)
        {
            if (Line(ref at) is not { } line)
            {
                return null;
            }

            lines.Add(line);
        }

        return lines;
    }

    private static CaptionLine? Line(ref ReadOnlySpan<byte> at)
    {
        if (at.Length < LineHeaderLength)
        {
            return null;
        }

        long pts = BinaryPrimitives.ReadInt64BigEndian(at);
        int length = BinaryPrimitives.ReadInt32BigEndian(at[8..]);

        if (length < 0 || at.Length - LineHeaderLength < length)
        {
            return null;
        }

        ReadOnlySpan<byte> text = at.Slice(LineHeaderLength, length);

        at = at[(LineHeaderLength + length)..];

        if (length is 0)
        {
            return new CaptionLine(pts, null);
        }

        try
        {
            return new CaptionLine(pts, Utf8.GetString(text));
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
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
