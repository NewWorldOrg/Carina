using System.Text;

using Carina.Broadcast.Text;

namespace Carina.Broadcast.DsmCc;

public static class EucJpText
{
    private const byte SingleShiftTwo = 0x8E;

    private const byte SingleShiftThree = 0x8F;

    public static byte[] ToUtf8(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetBytes(Decode(bytes));

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder(bytes.Length);
        int at = 0;

        while (at < bytes.Length)
        {
            at += Append(text, bytes[at..]);
        }

        return text.ToString();
    }

    private static int Append(StringBuilder text, ReadOnlySpan<byte> rest)
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
            text.Append(AribText.UnknownCharacter);

            return 3;
        }

        if (InUpperHalf(lead) && rest.Length > 1 && InUpperHalf(rest[1]))
        {
            AribText.Append(text, GraphicSet.Kanji, rest[..2]);

            return 2;
        }

        text.Append(AribText.UnknownCharacter);

        return 1;
    }

    private static bool InUpperHalf(byte code) => code is >= 0xA1 and <= 0xFE;
}
