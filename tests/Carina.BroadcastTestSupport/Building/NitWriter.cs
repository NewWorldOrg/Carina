namespace Carina.BroadcastTestSupport;

public sealed class NitWriter
{
    public byte[] NetworkDescriptors { get; init; } = [];

    public int? DeclaredNetworkDescriptorsLength { get; init; }

    public byte[][] TransportStreams { get; init; } = [];

    public int? DeclaredTransportStreamLoopLength { get; init; }

    public static byte[] TransportStream(
        int transportStreamId,
        int originalNetworkId,
        byte[] descriptors,
        int? declaredDescriptorsLength = null)
        => new ByteWriter()
            .Word(transportStreamId)
            .Word(originalNetworkId)
            .Word(0xF000 | (declaredDescriptorsLength ?? descriptors.Length))
            .Run(descriptors)
            .ToArray();

    public byte[] ToBody()
    {
        byte[] loop = TransportStreams.SelectMany(stream => stream).ToArray();

        return new ByteWriter()
            .Word(0xF000 | (DeclaredNetworkDescriptorsLength ?? NetworkDescriptors.Length))
            .Run(NetworkDescriptors)
            .Word(0xF000 | (DeclaredTransportStreamLoopLength ?? loop.Length))
            .Run(loop)
            .ToArray();
    }
}
