namespace Carina.BroadcastTestSupport;

public static class BxmlInfoWriter
{
    public const int BxmlDataComponentId = 0x000C;

    public const int EntryComponentTag = 0x40;

    public static byte[] Entry(
        bool autoStart,
        int documentResolution,
        int bmlMajorVersion,
        int bmlMinorVersion,
        int dataEventId,
        bool defaultVersion = false,
        bool useXml = false,
        int bxmlMajorVersion = 0,
        int bxmlMinorVersion = 0,
        int transmissionFormat = 0)
    {
        ByteWriter info = new ByteWriter()
            .Byte((transmissionFormat << 6) | 0x20 | (autoStart ? 0x10 : 0x00) | (documentResolution & 0x0F))
            .Byte((useXml ? 0x80 : 0x00) | (defaultVersion ? 0x40 : 0x00) | 0x0F);

        if (!defaultVersion)
        {
            info.Word(bmlMajorVersion).Word(bmlMinorVersion);
        }

        if (!defaultVersion && useXml)
        {
            info.Word(bxmlMajorVersion).Word(bxmlMinorVersion);
        }

        return info.Run(CarouselInfo(transmissionFormat, dataEventId)).ToArray();
    }

    public static byte[] NotEntry(int dataEventId, int transmissionFormat = 0)
        => new ByteWriter()
            .Byte((transmissionFormat << 6) | 0x1F)
            .Run(CarouselInfo(transmissionFormat, dataEventId))
            .ToArray();

    public static byte[] DataBroadcastStream(int pid, int componentTag, byte[] bxmlInfo)
        => PmtWriter.Stream(
            PmtWriter.DsmCcSections,
            pid,
            DescriptorWriter.Loop(
                PsiDescriptorWriter.StreamIdentifier(componentTag),
                PsiDescriptorWriter.DataComponent(BxmlDataComponentId, bxmlInfo)));

    private static byte[] CarouselInfo(int transmissionFormat, int dataEventId)
        => transmissionFormat switch
        {
            0 => new ByteWriter().Byte(((dataEventId & 0x0F) << 4) | 0x07).Byte(0x0F).ToArray(),
            1 => [0xFF],
            _ => [],
        };
}
