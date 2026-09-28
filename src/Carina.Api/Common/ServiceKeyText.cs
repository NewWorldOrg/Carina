using Carina.Domain.Channels;

namespace Carina.Api.Common;

public static class ServiceKeyText
{
    public static readonly string Description =
        $"A service is named by its network id and service id, each {NetworkId.MinValue} to {NetworkId.MaxValue}.";

    public static (NetworkId Network, ServiceId Service)? Read(int networkId, int serviceId)
        => networkId is < NetworkId.MinValue or > NetworkId.MaxValue
           || serviceId is < ServiceId.MinValue or > ServiceId.MaxValue
            ? null
            : (new NetworkId(networkId), new ServiceId(serviceId));
}
