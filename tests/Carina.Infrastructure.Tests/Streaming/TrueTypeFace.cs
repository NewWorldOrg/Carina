using System.Buffers.Binary;
using System.Text;

namespace Carina.Infrastructure.Tests.Streaming;

/// <summary>
/// Reads from a TrueType file the little the caption tests ask of a face: its family name, its em, the
/// advance of every glyph, and which glyph a character maps to.
/// </summary>
public sealed class TrueTypeFace
{
    private const int WindowsPlatform = 3;

    private const int UnicodeBmpEncoding = 1;

    private const int UnicodeFullEncoding = 10;

    private const int FamilyNameId = 1;

    private readonly byte[] file;

    private readonly IReadOnlyDictionary<string, (int Offset, int Length)> tables;

    private readonly IReadOnlyDictionary<int, int> characters;

    private TrueTypeFace(byte[] file)
    {
        this.file = file;
        tables = ReadTables(file);
        UnitsPerEm = UInt16(Table("head").Offset + 18);
        GlyphCount = UInt16(Table("maxp").Offset + 4);
        Advances = ReadAdvances();
        FamilyName = ReadFamilyName();
        characters = ReadCharacters();
    }

    public int UnitsPerEm { get; }

    public int GlyphCount { get; }

    public string FamilyName { get; }

    public IReadOnlyList<int> Advances { get; }

    public static TrueTypeFace Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return new TrueTypeFace(File.ReadAllBytes(path));
    }

    public int? GlyphOf(int codePoint)
        => characters.TryGetValue(codePoint, out int glyph) && glyph is not 0 ? glyph : null;

    public int? AdvanceOf(int codePoint)
        => GlyphOf(codePoint) is { } glyph ? Advances[glyph] : null;

    private (int Offset, int Length) Table(string tag)
        => tables.TryGetValue(tag, out (int Offset, int Length) table)
            ? table
            : throw new InvalidDataException($"The font has no '{tag}' table.");

    private static Dictionary<string, (int Offset, int Length)> ReadTables(byte[] file)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(4));
        Dictionary<string, (int Offset, int Length)> tables = new(StringComparer.Ordinal);

        for (int i = 0; i < count; i++)
        {
            int record = 12 + (i * 16);
            string tag = Encoding.ASCII.GetString(file, record, 4);
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(record + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(record + 12));
            tables[tag] = (offset, length);
        }

        return tables;
    }

    private int[] ReadAdvances()
    {
        int metrics = UInt16(Table("hhea").Offset + 34);
        int hmtx = Table("hmtx").Offset;
        int[] advances = new int[GlyphCount];

        for (int glyph = 0; glyph < GlyphCount; glyph++)
        {
            int metric = Math.Min(glyph, metrics - 1);
            advances[glyph] = UInt16(hmtx + (metric * 4));
        }

        return advances;
    }

    private string ReadFamilyName()
    {
        int name = Table("name").Offset;
        int count = UInt16(name + 2);
        int strings = name + UInt16(name + 4);

        for (int i = 0; i < count; i++)
        {
            int record = name + 6 + (i * 12);

            if (UInt16(record) != WindowsPlatform || UInt16(record + 2) != UnicodeBmpEncoding || UInt16(record + 6) != FamilyNameId)
            {
                continue;
            }

            int length = UInt16(record + 8);
            int offset = UInt16(record + 10);

            return Encoding.BigEndianUnicode.GetString(file, strings + offset, length);
        }

        throw new InvalidDataException("The font names no family for Windows in Unicode.");
    }

    private Dictionary<int, int> ReadCharacters()
    {
        int cmap = Table("cmap").Offset;
        int count = UInt16(cmap + 2);
        int? bmp = null;
        int? full = null;

        for (int i = 0; i < count; i++)
        {
            int record = cmap + 4 + (i * 8);
            int platform = UInt16(record);
            int encoding = UInt16(record + 2);
            int subtable = cmap + (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(record + 4));

            if (platform == WindowsPlatform && encoding == UnicodeFullEncoding)
            {
                full = subtable;
            }
            else if (platform == WindowsPlatform && encoding == UnicodeBmpEncoding)
            {
                bmp = subtable;
            }
        }

        if (full is { } segmented && UInt16(segmented) is 12)
        {
            return ReadFormat12(segmented);
        }

        if (bmp is { } ranged && UInt16(ranged) is 4)
        {
            return ReadFormat4(ranged);
        }

        throw new InvalidDataException("The font maps no Unicode characters in a format this reader knows.");
    }

    private Dictionary<int, int> ReadFormat4(int subtable)
    {
        int segments = UInt16(subtable + 6) / 2;
        int ends = subtable + 14;
        int starts = ends + (segments * 2) + 2;
        int deltas = starts + (segments * 2);
        int rangeOffsets = deltas + (segments * 2);
        Dictionary<int, int> characters = [];

        for (int segment = 0; segment < segments; segment++)
        {
            int end = UInt16(ends + (segment * 2));
            int start = UInt16(starts + (segment * 2));
            int delta = UInt16(deltas + (segment * 2));
            int rangeOffsetAt = rangeOffsets + (segment * 2);
            int rangeOffset = UInt16(rangeOffsetAt);

            if (start == 0xFFFF)
            {
                continue;
            }

            for (int code = start; code <= end; code++)
            {
                int glyph;

                if (rangeOffset is 0)
                {
                    glyph = (code + delta) & 0xFFFF;
                }
                else
                {
                    int at = rangeOffsetAt + rangeOffset + ((code - start) * 2);
                    int indexed = UInt16(at);
                    glyph = indexed is 0 ? 0 : (indexed + delta) & 0xFFFF;
                }

                if (glyph is not 0)
                {
                    characters[code] = glyph;
                }
            }
        }

        return characters;
    }

    private Dictionary<int, int> ReadFormat12(int subtable)
    {
        int groups = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(subtable + 12));
        Dictionary<int, int> characters = [];

        for (int group = 0; group < groups; group++)
        {
            int record = subtable + 16 + (group * 12);
            int start = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(record));
            int end = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(record + 4));
            int first = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(record + 8));

            for (int code = start; code <= end; code++)
            {
                characters[code] = first + (code - start);
            }
        }

        return characters;
    }

    private int UInt16(int at)
        => BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(at));
}
