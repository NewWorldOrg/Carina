using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveEndingRecord : ILiveEnding
{
    private LiveSupplyEnding? current;

    public LiveSupplyEnding? Current => Volatile.Read(ref current);

    public void Note(LiveSupplyEnding ending)
    {
        ArgumentNullException.ThrowIfNull(ending);

        Interlocked.CompareExchange(ref current, ending, null);
    }
}
