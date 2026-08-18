using Carina.Broadcast.Descriptors;

namespace Carina.BroadcastTestSupport;

public static class SiDescriptorWriter
{
    public static byte[] NetworkName(byte[] name) => DescriptorWriter.Of(DescriptorTags.NetworkName, name);

    public static byte[] ServiceList(params (int ServiceId, int ServiceType)[] services)
    {
        var payload = new ByteWriter();

        foreach ((int serviceId, int serviceType) in services)
        {
            payload.Word(serviceId).Byte(serviceType);
        }

        return DescriptorWriter.Of(DescriptorTags.ServiceList, payload.ToArray());
    }

    public static byte[] Service(int serviceType, byte[] providerName, byte[] serviceName)
        => DescriptorWriter.Of(
            DescriptorTags.Service,
            new ByteWriter()
                .Byte(serviceType)
                .Byte(providerName.Length)
                .Run(providerName)
                .Byte(serviceName.Length)
                .Run(serviceName)
                .ToArray());

    public static byte[] TransportStreamInformation(
        int remoteControlKeyId,
        byte[] name,
        params (int Info, int[] ServiceIds)[] transmissionTypes)
    {
        ByteWriter payload = new ByteWriter()
            .Byte(remoteControlKeyId)
            .Byte((name.Length << 2) | transmissionTypes.Length)
            .Run(name);

        foreach ((int info, int[]? serviceIds) in transmissionTypes)
        {
            payload.Byte(info).Byte(serviceIds.Length);

            foreach (int serviceId in serviceIds)
            {
                payload.Word(serviceId);
            }
        }

        return DescriptorWriter.Of(DescriptorTags.TransportStreamInformation, payload.ToArray());
    }

    public static byte[] PartialReception(params int[] serviceIds)
    {
        var payload = new ByteWriter();

        foreach (int serviceId in serviceIds)
        {
            payload.Word(serviceId);
        }

        return DescriptorWriter.Of(DescriptorTags.PartialReception, payload.ToArray());
    }
}
