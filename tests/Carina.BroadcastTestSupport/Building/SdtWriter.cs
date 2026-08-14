namespace Carina.BroadcastTestSupport;

public sealed class SdtWriter
{
    public int OriginalNetworkId { get; init; }

    public byte[][] Services { get; init; } = [];

    public static byte[] Service(
        int serviceId,
        byte[] descriptors,
        bool carriesScheduleEvents = true,
        bool carriesPresentFollowingEvents = true,
        int runningStatus = 4,
        bool isConditionalAccess = false,
        int? declaredDescriptorsLength = null)
    {
        var length = declaredDescriptorsLength ?? descriptors.Length;

        return new ByteWriter()
            .Word(serviceId)
            .Byte(0xFC | (carriesScheduleEvents ? 0x02 : 0) | (carriesPresentFollowingEvents ? 0x01 : 0))
            .Byte((runningStatus << 5) | (isConditionalAccess ? 0x10 : 0) | (length >> 8))
            .Byte(length & 0xFF)
            .Run(descriptors)
            .ToArray();
    }

    public byte[] ToBody()
        => new ByteWriter()
            .Word(OriginalNetworkId)
            .Byte(0xFF)
            .Run(Services.SelectMany(service => service).ToArray())
            .ToArray();
}
