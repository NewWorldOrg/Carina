namespace Carina.BroadcastTestSupport;

public static class PsiDescriptorWriter
{
    public const int StreamIdentifierTag = 0x52;

    public const int DataComponentTag = 0xFD;

    public const int CaptionDataComponentId = 0x0008;

    public const int FirstCaptionComponentTag = 0x30;

    public const int FirstSuperimposeComponentTag = 0x38;

    public static byte[] StreamIdentifier(int componentTag)
        => DescriptorWriter.Of(StreamIdentifierTag, (byte)componentTag);

    public static byte[] DataComponent(int dataComponentId, params byte[] additional)
        => DescriptorWriter.Of(
            DataComponentTag,
            new ByteWriter().Word(dataComponentId).Run(additional).ToArray());
}
