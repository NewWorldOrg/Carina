using Carina.Contracts;

namespace Carina.Domain.DriverStatus;

public sealed record DriverObservation
{
    public static readonly DriverObservation NotConnected =
        new(DriverConnection.NotConnected, null, []);

    private DriverObservation(
        DriverConnection connection,
        DriverHello? hello,
        IReadOnlyList<string> missingCapabilities)
    {
        Connection = connection;
        Hello = hello;
        MissingCapabilities = missingCapabilities;
    }

    public DriverConnection Connection { get; private init; }

    public DriverHello? Hello { get; }

    public IReadOnlyList<string> MissingCapabilities { get; }

    public bool DriverUpdateRequired
        => MissingCapabilities.Count > 0
           || (Hello is { } hello && hello.ProtocolVersion < DriverProtocol.Version);

    public static DriverObservation Of(DriverHello hello, IReadOnlyList<string> missingCapabilities)
    {
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(missingCapabilities);

        DriverConnection connection = hello.Draining ? DriverConnection.Draining : DriverConnection.Connected;

        return new DriverObservation(connection, hello, missingCapabilities);
    }

    public DriverObservation WhileDraining()
        => this with { Connection = DriverConnection.Draining };
}
