namespace Carina.Broadcast.Text;

/// <summary>
/// Reads the caption data a caption stream carries in each packet: a data group, and in it the statement of
/// the first language, as the bytes of its body units joined in order.
/// </summary>
public static class AribCaptionData
{
    public const byte CaptionIdentifier = 0x80;

    public const byte PrivateStreamIdentifier = 0xFF;

    public const int FirstLanguage = 1;

    public const byte UnitSeparator = 0x1F;

    public const byte BodyUnit = 0x20;

    private const int GroupHeaderLength = 5;

    private const int UnitHeaderLength = 5;

    private const int ShownAtLength = 5;

    private const int LoopLengthLength = 3;

    /// <summary>
    /// The body of the statement in the first language a packet carries, empty for one that carries no body
    /// unit, or null for anything else: the management data, another language, a superimposition, or data
    /// whose lengths do not add up.
    /// </summary>
    public static byte[]? Statement(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3 || data[0] != CaptionIdentifier || data[1] != PrivateStreamIdentifier)
        {
            return null;
        }

        int skipped = 3 + (data[2] & 0x0F);

        if (data.Length < skipped + GroupHeaderLength)
        {
            return null;
        }

        ReadOnlySpan<byte> group = data[skipped..];
        int language = (group[0] >> 2) & 0x1F;
        int size = (group[3] << 8) | group[4];

        if (language != FirstLanguage || group.Length < GroupHeaderLength + size)
        {
            return null;
        }

        return Units(group.Slice(GroupHeaderLength, size));
    }

    private static byte[]? Units(ReadOnlySpan<byte> statement)
    {
        if (statement.IsEmpty)
        {
            return null;
        }

        int timing = statement[0] >> 6;
        int at = 1 + (timing is 1 or 2 ? ShownAtLength : 0);

        if (statement.Length < at + LoopLengthLength)
        {
            return null;
        }

        int loop = (statement[at] << 16) | (statement[at + 1] << 8) | statement[at + 2];
        at += LoopLengthLength;

        if (statement.Length < at + loop)
        {
            return null;
        }

        return Bodies(statement.Slice(at, loop));
    }

    private static byte[]? Bodies(ReadOnlySpan<byte> units)
    {
        List<byte> body = [];
        int at = 0;

        while (at < units.Length)
        {
            if (units.Length - at < UnitHeaderLength || units[at] != UnitSeparator)
            {
                return null;
            }

            byte parameter = units[at + 1];
            int size = (units[at + 2] << 16) | (units[at + 3] << 8) | units[at + 4];
            at += UnitHeaderLength;

            if (units.Length - at < size)
            {
                return null;
            }

            if (parameter == BodyUnit)
            {
                body.AddRange(units.Slice(at, size));
            }

            at += size;
        }

        return [.. body];
    }
}
