using Carina.Contracts;
using Carina.Domain.DriverStatus;

namespace Carina.Api.Responder.DriverStatus;

public sealed record DriverStatusResponder(
    DriverConnection Connection,
    DriverHelloResponder? Hello,
    int AppProtocolVersion,
    IReadOnlyList<string> MissingCapabilities,
    bool DriverUpdateRequired,
    DateTimeOffset ObservedAt)
{
    public static DriverStatusResponder Of(DriverStatusSnapshot snapshot)
    {
        DriverObservation observation = snapshot.Observation;
        DriverHelloResponder? hello = observation.Hello is { } seen
            ? new DriverHelloResponder(
                seen.ProtocolVersion,
                seen.InstanceId,
                seen.Capabilities,
                seen.Draining)
            : null;

        return new DriverStatusResponder(
            observation.Connection,
            hello,
            DriverProtocol.Version,
            observation.MissingCapabilities,
            observation.DriverUpdateRequired,
            snapshot.ObservedAt);
    }
}
