namespace Carina.Driver.Transport;

/// <summary>
/// Counts the packets a stream lost, one stream at a time.
/// </summary>
/// <remarks>
/// The sender advances a four-bit counter for every packet it sends on a stream, so
/// a value that skips means packets went missing between here and there. Four bits
/// is fifteen, which makes the count a floor rather than a total on a long outage —
/// the point is to know that something was lost, and roughly how much.
///
/// Measuring here, while the packets go past on their way to the file, is what
/// makes a broken recording findable later instead of discoverable on playback.
/// </remarks>
public sealed class ContinuityCounterTracker
{
    private const int CounterWrap = 16;

    private readonly Dictionary<int, int> lastCounter = [];
    private readonly Dictionary<int, long> dropsByPid = [];

    /// <summary>Packets that carried content, padding excluded.</summary>
    public long Packets { get; private set; }

    /// <summary>Packets the sender sent that never arrived.</summary>
    public long Drops { get; private set; }

    /// <summary>Packets that arrived twice. Not a loss, and not counted as one.</summary>
    public long Duplicates { get; private set; }

    /// <summary>What <paramref name="pid"/> lost by itself.</summary>
    public long DropsFor(int pid) => dropsByPid.GetValueOrDefault(pid);

    /// <summary>Takes one packet into account.</summary>
    public void Observe(TsPacket packet)
    {
        // Padding fills the multiplex to a constant rate. Its counter does not
        // advance dependably, so measuring it would invent losses.
        if (packet.IsNull)
        {
            return;
        }

        Packets++;

        if (!lastCounter.TryGetValue(packet.Pid, out var previous))
        {
            lastCounter[packet.Pid] = packet.ContinuityCounter;
            return;
        }

        // A packet with no payload leaves the counter where it was, so the same
        // value again is what the standard asks for rather than a repeat.
        if (!packet.HasPayload && packet.ContinuityCounter == previous)
        {
            return;
        }

        if (packet.ContinuityCounter == previous)
        {
            Duplicates++;
            return;
        }

        var expected = (previous + 1) % CounterWrap;
        if (packet.ContinuityCounter != expected)
        {
            var missing = (packet.ContinuityCounter - expected + CounterWrap) % CounterWrap;
            Drops += missing;
            dropsByPid[packet.Pid] = DropsFor(packet.Pid) + missing;
        }

        lastCounter[packet.Pid] = packet.ContinuityCounter;
    }
}
