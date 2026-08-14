using System.Threading.Channels;

using Carina.Contracts;
using Carina.Driver.Tuning;

namespace Carina.Driver.Sessions;

public sealed class StreamCutException(
    SessionStopReason reason,
    string message,
    Exception? cause
) : IOException(message, cause)
{
    public SessionStopReason Reason { get; } = reason;
}

public sealed class PiggybackTunerDevice(TunerSession host, SessionSubscription seat)
    : ITunerDevice
{
    public long Overflows => host.DeviceOverflows;

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        try
        {
            return seat.Reader.ReadAsync(cancellationToken).AsTask().GetAwaiter().GetResult();
        }
        catch (ChannelClosedException closed)
        {
            throw Cut(closed.InnerException);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Cut(null);
        }
    }

    public void Dispose() => host.Broadcaster.Unsubscribe(seat);

    private StreamCutException Cut(Exception? cause) =>
        new(
            host.StopReason,
            $"The stream of '{host.SessionId}' on the tuner '{host.DeviceId}' ended, so this one ends here and is incomplete"
                + (cause is null ? "." : $": {cause.Message}"),
            cause
        );
}
