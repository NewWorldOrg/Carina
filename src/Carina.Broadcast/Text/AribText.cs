using System.Text;

namespace Carina.Broadcast.Text;

public static class AribText
{
    public const char UnknownCharacter = '□';

    private const byte Escape = 0x1B;

    private const byte LockingShiftZero = 0x0F;

    private const byte LockingShiftOne = 0x0E;

    private const byte SingleShiftTwo = 0x19;

    private const byte SingleShiftThree = 0x1D;

    private const byte ActivePositionForward = 0x16;

    private const byte ActivePositionSet = 0x1C;

    private const byte Delete = 0x7F;

    public static string Decode(ReadOnlyMemory<byte> bytes) => Decode(bytes.Span);

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var designated = new[]
        {
            GraphicSet.Kanji,
            GraphicSet.Alphanumeric,
            GraphicSet.Hiragana,
            GraphicSet.Katakana,
        };

        var left = 0;
        var right = 2;
        var single = -1;
        var text = new StringBuilder(bytes.Length);
        var at = 0;

        while (at < bytes.Length)
        {
            var code = bytes[at];

            if (code == Escape)
            {
                at = ReadEscape(bytes, at + 1, designated, ref left, ref right);

                continue;
            }

            if (code < 0x20)
            {
                switch (code)
                {
                    case LockingShiftZero:
                        left = 0;

                        break;
                    case LockingShiftOne:
                        left = 1;

                        break;
                    case SingleShiftTwo:
                        single = 2;

                        break;
                    case SingleShiftThree:
                        single = 3;

                        break;
                }

                at += code switch
                {
                    ActivePositionForward => 2,
                    ActivePositionSet => 3,
                    _ => 1,
                };

                continue;
            }

            if (code is >= 0x80 and <= 0x9F)
            {
                at = ReadC1(bytes, at);

                continue;
            }

            if (code is 0x20 or 0xA0)
            {
                text.Append(' ');
                single = -1;
                at++;

                continue;
            }

            if (code is Delete or 0xFF)
            {
                single = -1;
                at++;

                continue;
            }

            var set = single >= 0 ? designated[single] : designated[code >= 0x80 ? right : left];
            single = -1;
            var width = AribGraphicSets.Width(set);

            if (at + width > bytes.Length)
            {
                break;
            }

            Append(text, set, bytes.Slice(at, width));
            at += width;
        }

        return text.ToString();
    }

    private static void Append(StringBuilder text, GraphicSet set, ReadOnlySpan<byte> code)
    {
        if (AribGraphicSets.IsDrcs(set))
        {
            return;
        }

        if (code.Length == 2)
        {
            var row = (code[0] & 0x7F) - 0x20;
            var cell = (code[1] & 0x7F) - 0x20;

            text.Append(
                set == GraphicSet.Kanji && JisX0208.TryMap(row, cell, out var kanji)
                    ? kanji
                    : UnknownCharacter);

            return;
        }

        var index = (code[0] & 0x7F) - AribGraphicSets.FirstCode;

        switch (set)
        {
            case GraphicSet.Alphanumeric:
                text.Append((char)(code[0] & 0x7F));

                break;
            case GraphicSet.Hiragana:
                Append(text, AribGraphicSets.Hiragana, index);

                break;
            case GraphicSet.Katakana:
                Append(text, AribGraphicSets.Katakana, index);

                break;
            case GraphicSet.HalfWidthKatakana:
                text.Append(index is >= 0 and <= 0x3E ? (char)(0xFF61 + index) : UnknownCharacter);

                break;
            default:
                text.Append(UnknownCharacter);

                break;
        }
    }

    private static void Append(StringBuilder text, string set, int index)
    {
        var mapped = index >= 0 && index < set.Length ? set[index] : '\0';
        text.Append(mapped == '\0' ? UnknownCharacter : mapped);
    }

    private static int ReadEscape(
        ReadOnlySpan<byte> bytes,
        int at,
        GraphicSet[] designated,
        ref int left,
        ref int right)
    {
        if (at >= bytes.Length)
        {
            return bytes.Length;
        }

        switch (bytes[at])
        {
            case 0x6E:
                left = 2;

                return at + 1;
            case 0x6F:
                left = 3;

                return at + 1;
            case 0x7C:
                right = 3;

                return at + 1;
            case 0x7D:
                right = 2;

                return at + 1;
            case 0x7E:
                right = 1;

                return at + 1;
        }

        if (bytes[at] == 0x24)
        {
            return ReadTwoByteDesignation(bytes, at + 1, designated);
        }

        if (bytes[at] is >= 0x28 and <= 0x2B)
        {
            return ReadOneByteDesignation(bytes, at + 1, designated, bytes[at] - 0x28);
        }

        return at + 1;
    }

    private static int ReadTwoByteDesignation(ReadOnlySpan<byte> bytes, int at, GraphicSet[] designated)
    {
        if (at >= bytes.Length)
        {
            return bytes.Length;
        }

        if (bytes[at] is < 0x29 or > 0x2B)
        {
            designated[0] = AribGraphicSets.TwoByteSet(bytes[at]);

            return at + 1;
        }

        var slot = bytes[at] - 0x28;

        if (at + 1 >= bytes.Length)
        {
            return bytes.Length;
        }

        if (bytes[at + 1] != 0x20)
        {
            designated[slot] = AribGraphicSets.TwoByteSet(bytes[at + 1]);

            return at + 2;
        }

        if (at + 2 >= bytes.Length)
        {
            return bytes.Length;
        }

        designated[slot] = GraphicSet.TwoByteDrcs;

        return at + 3;
    }

    private static int ReadOneByteDesignation(ReadOnlySpan<byte> bytes, int at, GraphicSet[] designated, int slot)
    {
        if (at >= bytes.Length)
        {
            return bytes.Length;
        }

        if (bytes[at] != 0x20)
        {
            designated[slot] = AribGraphicSets.OneByteSet(bytes[at]);

            return at + 1;
        }

        if (at + 1 >= bytes.Length)
        {
            return bytes.Length;
        }

        designated[slot] = bytes[at + 1] == 0x70 ? GraphicSet.Macro : GraphicSet.OneByteDrcs;

        return at + 2;
    }

    private static int ReadC1(ReadOnlySpan<byte> bytes, int at)
    {
        switch (bytes[at])
        {
            case 0x8B or 0x91 or 0x93 or 0x94 or 0x97 or 0x98:
                return at + 2;
            case 0x90 or 0x92:
                return at + 1 < bytes.Length && bytes[at + 1] == 0x20 ? at + 3 : at + 2;
            case 0x9D:
                return at + 3;
            case 0x95:
                return Until(bytes, at + 1, terminator => terminator == 0x4F);
            case 0x9B:
                return Until(bytes, at + 1, terminator => terminator is >= 0x40 and <= 0x6F);
            default:
                return at + 1;
        }
    }

    private static int Until(ReadOnlySpan<byte> bytes, int at, Func<byte, bool> terminates)
    {
        while (at < bytes.Length)
        {
            if (terminates(bytes[at]))
            {
                return at + 1;
            }

            at++;
        }

        return bytes.Length;
    }
}
