namespace Carina.BroadcastTestSupport;

public static class CaptionWriter
{
    public const byte CaptionDataIdentifier = 0x80;

    public const byte SuperimposeDataIdentifier = 0x81;

    public const int ManagementGroup = 0x00;

    public const int StatementGroup = 0x01;

    public const byte ClearScreen = 0x0C;

    public const byte ActivePositionSet = 0x1C;

    public const string Japanese = "jpn";

    public static byte[] Management(string language = Japanese)
    {
        ArgumentNullException.ThrowIfNull(language);

        if (language.Length is not 3)
        {
            throw new ArgumentException("A language is its three-letter code.", nameof(language));
        }

        return DataGroup(
            ManagementGroup,
            new ByteWriter()
                .Byte(0x00)
                .Byte(0x01)
                .Byte(0x00)
                .Run([.. language.Select(letter => (byte)letter)])
                .Byte(0x00)
                .Byte(0x00)
                .Word(0x0000)
                .ToArray());
    }

    public static byte[] Statement(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);

        byte[] unit = new ByteWriter()
            .Byte(0x1F)
            .Byte(0x20)
            .Byte(body.Length >> 16)
            .Word(body.Length & 0xFFFF)
            .Run(body)
            .ToArray();

        return DataGroup(
            StatementGroup,
            new ByteWriter()
                .Byte(0x00)
                .Byte(unit.Length >> 16)
                .Word(unit.Length & 0xFFFF)
                .Run(unit)
                .ToArray());
    }

    public static byte[] Positioned(int row, int column, AribTextWriter text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return
        [
            ClearScreen,
            ActivePositionSet,
            (byte)(0x40 + row),
            (byte)(0x40 + column),
            .. text.ToArray(),
        ];
    }

    public static byte[] DataGroup(int groupId, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        byte[] group = new ByteWriter()
            .Byte(groupId << 2)
            .Byte(0x00)
            .Byte(0x00)
            .Word(data.Length)
            .Run(data)
            .ToArray();

        return new ByteWriter().Run(group).Word(ReferenceCrc16.Compute(group)).ToArray();
    }

    public static byte[] Carried(byte dataIdentifier, byte[] dataGroup)
    {
        ArgumentNullException.ThrowIfNull(dataGroup);

        return new ByteWriter()
            .Byte(dataIdentifier)
            .Byte(0xFF)
            .Byte(0xF0)
            .Run(dataGroup)
            .ToArray();
    }
}
