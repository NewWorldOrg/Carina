using Carina.Driver.Tuning;

namespace Carina.Driver.Sessions;

public sealed class LeasedTunerDevice(ITunerDevice tuner) : ITunerDevice
{
    public long Overflows => tuner.Overflows;

    public byte[] Read(int count, CancellationToken cancellationToken) =>
        tuner.Read(count, cancellationToken);

    public void Dispose() { }
}
