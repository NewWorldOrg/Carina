using System.Collections.Frozen;
using System.Text;

using Carina.Broadcast.Text;

namespace Carina.Broadcast.DsmCc;

public static class EucJpText
{
    private const byte SingleShiftTwo = 0x8E;

    private const byte SingleShiftThree = 0x8F;

    private const int HalfWidthKatakanaFirst = 0xFF61;

    private const int HalfWidthKatakanaLast = 0xFF9F;

    private const int UpperHalf = 0xA0;

    private const char Geta = '\u3013';

    private static readonly Lazy<FrozenDictionary<int, ushort>> Cells = new(CellsOfEveryCharacter);

    public static byte[] ToUtf8(ReadOnlySpan<byte> bytes) => ToUtf8(bytes, out _);

    public static byte[] ToUtf8(ReadOnlySpan<byte> bytes, out int substitutions)
        => Encoding.UTF8.GetBytes(Decode(bytes, out substitutions));

    public static string Decode(ReadOnlySpan<byte> bytes) => Decode(bytes, out _);

    public static byte[] Encode(string text) => Encode(text, out _);

    /// <summary>
    /// The text in EUC-JP over the same tables the decoding reads, a character no table holds being written as the
    /// geta mark and counted in <paramref name="substitutions"/>.
    /// </summary>
    public static byte[] Encode(string text, out int substitutions)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<byte> bytes = new(text.Length * 2);
        substitutions = 0;

        foreach (Rune rune in text.EnumerateRunes())
        {
            if (!Put(bytes, rune.Value))
            {
                Put(bytes, Geta);
                substitutions++;
            }
        }

        return [.. bytes];
    }

    public static string Decode(ReadOnlySpan<byte> bytes, out int substitutions)
    {
        var text = new StringBuilder(bytes.Length);
        int at = 0;
        substitutions = 0;

        while (at < bytes.Length)
        {
            at += Append(text, bytes[at..], ref substitutions);
        }

        return text.ToString();
    }

    private static int Append(StringBuilder text, ReadOnlySpan<byte> rest, ref int substitutions)
    {
        byte lead = rest[0];

        if (lead < 0x80)
        {
            text.Append((char)lead);

            return 1;
        }

        if (lead == SingleShiftTwo && rest.Length > 1 && rest[1] is >= 0xA1 and <= 0xDF)
        {
            AribText.Append(text, GraphicSet.HalfWidthKatakana, rest.Slice(1, 1));

            return 2;
        }

        if (lead == SingleShiftThree && rest.Length > 2 && InUpperHalf(rest[1]) && InUpperHalf(rest[2]))
        {
            return Substitute(text, 3, ref substitutions);
        }

        if (!InUpperHalf(lead) || rest.Length < 2 || !InUpperHalf(rest[1]))
        {
            return Substitute(text, 1, ref substitutions);
        }

        if (!IsAssigned(lead - 0xA0, rest[1] - 0xA0))
        {
            return Substitute(text, 2, ref substitutions);
        }

        AribText.Append(text, GraphicSet.Kanji, rest[..2]);

        return 2;
    }

    private static int Substitute(StringBuilder text, int consumed, ref int substitutions)
    {
        text.Append(AribText.UnknownCharacter);
        substitutions++;

        return consumed;
    }

    private static bool IsAssigned(int row, int cell)
        => JisX0208.TryMap(row, cell, out _) || AribSymbols.TryMap(row, cell, out _);

    private static bool InUpperHalf(byte code) => code is >= 0xA1 and <= 0xFE;

    private static bool Put(List<byte> bytes, int code)
    {
        if (code < 0x80)
        {
            bytes.Add((byte)code);

            return true;
        }

        if (code is >= HalfWidthKatakanaFirst and <= HalfWidthKatakanaLast)
        {
            bytes.Add(SingleShiftTwo);
            bytes.Add((byte)(code - HalfWidthKatakanaFirst + 0xA1));

            return true;
        }

        if (!Cells.Value.TryGetValue(code, out ushort cell))
        {
            return false;
        }

        bytes.Add((byte)(cell >> 8));
        bytes.Add((byte)cell);

        return true;
    }

    private static FrozenDictionary<int, ushort> CellsOfEveryCharacter()
    {
        Dictionary<int, ushort> cells = [];

        for (int row = JisX0208.FirstRow; row <= AribSymbols.LastRow; row++)
        {
            for (int cell = 1; cell <= JisX0208.CellsPerRow; cell++)
            {
                if (CodeAt(row, cell) is { } code)
                {
                    cells.TryAdd(code, (ushort)(((row + UpperHalf) << 8) | (cell + UpperHalf)));
                }
            }
        }

        return cells.ToFrozenDictionary();
    }

    private static int? CodeAt(int row, int cell)
    {
        if (JisX0208.TryMap(row, cell, out char character))
        {
            return character;
        }

        return AribSymbols.TryMap(row, cell, out string symbol) ? char.ConvertToUtf32(symbol, 0) : null;
    }
}
