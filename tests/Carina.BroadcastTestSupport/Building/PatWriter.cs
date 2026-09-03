namespace Carina.BroadcastTestSupport;

public static class PatWriter
{
    public const int Pid = 0x0000;

    public const int TableId = 0x00;

    public static byte[] Section(int transportStreamId, params (int ProgramNumber, int PmtPid)[] programs)
    {
        var body = new ByteWriter();

        foreach ((int programNumber, int pmtPid) in programs)
        {
            body.Word(programNumber).Word(0xE000 | pmtPid);
        }

        return new SectionWriter
        {
            TableId = TableId,
            TableIdExtension = transportStreamId,
            Body = body.ToArray(),
        }.ToBytes();
    }
}
