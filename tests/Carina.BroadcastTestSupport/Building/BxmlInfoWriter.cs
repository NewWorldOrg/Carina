namespace Carina.BroadcastTestSupport;

public static class BxmlInfoWriter
{
    public const int BxmlDataComponentId = 0x000C;

    public const int EntryComponentTag = 0x40;

    public static byte[] Entry(bool autoStart, int documentResolution, int bmlMajorVersion, int bmlMinorVersion, int dataEventId)
        => new ByteWriter()
            .Byte(0x20 | (autoStart ? 0x10 : 0x00) | (documentResolution & 0x0F))
            .Byte(0x0F)
            .Word(bmlMajorVersion)
            .Word(bmlMinorVersion)
            .Run(CarouselInfo(dataEventId))
            .ToArray();

    public static byte[] NotEntry(int dataEventId)
        => new ByteWriter()
            .Byte(0x1F)
            .Run(CarouselInfo(dataEventId))
            .ToArray();

    public static byte[] DataBroadcastStream(int pid, int componentTag, byte[] bxmlInfo)
        => PmtWriter.Stream(
            PmtWriter.DsmCcSections,
            pid,
            DescriptorWriter.Loop(
                PsiDescriptorWriter.StreamIdentifier(componentTag),
                PsiDescriptorWriter.DataComponent(BxmlDataComponentId, bxmlInfo)));

    private static byte[] CarouselInfo(int dataEventId)
        => new ByteWriter()
            .Byte(((dataEventId & 0x0F) << 4) | 0x07)
            .Byte(0x1F)
            .ToArray();
}
