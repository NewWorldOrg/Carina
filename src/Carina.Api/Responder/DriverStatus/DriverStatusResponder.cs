using Carina.Domain.DriverStatus;

namespace Carina.Api.Responder.DriverStatus;

public sealed record DriverStatusResponder(DriverConnection Connection, DateTimeOffset ObservedAt);
