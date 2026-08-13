namespace Carina.Api.Responder.DriverStatus;

public sealed record DriverHelloResponder(
    int ProtocolVersion,
    string? InstanceId,
    IReadOnlyList<string> Capabilities,
    bool Draining);
