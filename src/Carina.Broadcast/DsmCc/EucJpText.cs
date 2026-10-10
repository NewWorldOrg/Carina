using System.Text;

using Carina.Broadcast.Text;

namespace Carina.Broadcast.DsmCc;

public static class EucJpText
{
    private const byte SingleShiftTwo = 0x8E;

    private const byte SingleShiftThree = 0x8F;

    public static byte[] ToUtf8(ReadOnlySpan<byte> bytes) => ToUtf8(bytes, out _);

    public static byte[] ToUtf8(ReadOnlySpan<byte> bytes, out int substitutions)
        => Encoding.UTF8.GetBytes(Decode(bytes, out substitutions));

    public static string Decode(ReadOnlySpan<byte> bytes) => Decode(bytes, out _);

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
}
