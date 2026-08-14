namespace Carina.Broadcast.Tests.Building;

public static class DescriptorWriter
{
    public static byte[] Of(int tag, params byte[] payload)
        => new ByteWriter().Byte(tag).Byte(payload.Length).Run(payload).ToArray();

    public static byte[] Overrunning(int tag, int declaredLength, params byte[] payload)
        => new ByteWriter().Byte(tag).Byte(declaredLength).Run(payload).ToArray();

    public static byte[] Loop(params byte[][] descriptors)
        => descriptors.SelectMany(descriptor => descriptor).ToArray();
}
