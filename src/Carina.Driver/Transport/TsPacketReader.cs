namespace Carina.Driver.Transport;

/// <summary>
/// One transport stream packet, as far as the driver needs to understand it.
/// </summary>
/// <param name="Pid">Which stream inside the multiplex this packet belongs to.</param>
/// <param name="ContinuityCounter">The counter the sender increments per packet, per stream.</param>
/// <param name="HasPayload">Whether the packet carries payload for its stream.</param>
/// <remarks>
/// The driver reads only the header. What the payload means is the app's business:
/// the privileged process stays at the byte layer, so that understanding broadcast
/// tables never becomes a reason to run as root.
/// </remarks>
public readonly record struct TsPacket(int Pid, int ContinuityCounter, bool HasPayload)
{
    /// <summary>The padding stream. It carries no continuity worth counting.</summary>
    public const int NullPid = 0x1FFF;

    /// <summary>Whether this is padding rather than content.</summary>
    public bool IsNull => Pid is NullPid;
}

/// <summary>
/// Turns a stream of bytes into packets, and says when it lost its place.
/// </summary>
/// <remarks>
/// The stream can start mid-packet and can break in the middle, so alignment is
/// something to find and to re-find rather than to assume. A sync byte alone is not
/// a boundary — payload contains 0x47 like any other value — so a candidate is only
/// accepted when the byte 188 further on is a sync byte too.
///
/// Losing alignment is counted rather than hidden. A stream that keeps resyncing is
/// a stream with a real problem, and the count is what makes that visible.
/// </remarks>
public sealed class TsPacketReader
{
    /// <summary>Every packet is this long, always.</summary>
    public const int PacketLength = 188;

    private const byte SyncByte = 0x47;

    private readonly List<byte> buffer = [];
    private bool aligned;

    /// <summary>How many times the reader had to look for the boundary again.</summary>
    public int ResyncCount { get; private set; }

    /// <summary>How many bytes were dropped while looking for a boundary.</summary>
    public long DiscardedBytes { get; private set; }

    /// <summary>Reads whatever whole packets <paramref name="bytes"/> completes.</summary>
    public IReadOnlyList<TsPacket> Read(ReadOnlySpan<byte> bytes)
    {
        buffer.AddRange(bytes);

        var packets = new List<TsPacket>();
        while (true)
        {
            if (!aligned && !TryAlign())
            {
                break;
            }

            if (buffer.Count < PacketLength)
            {
                break;
            }

            if (buffer[0] is not SyncByte)
            {
                // The boundary was where it should be and no longer is, so the
                // stream broke rather than merely started late.
                aligned = false;
                ResyncCount++;
                continue;
            }

            packets.Add(ReadHeader());
            buffer.RemoveRange(0, PacketLength);
        }

        return packets;
    }

    private bool TryAlign()
    {
        for (var offset = 0; offset + PacketLength <= buffer.Count; offset++)
        {
            if (buffer[offset] is not SyncByte)
            {
                continue;
            }

            // Confirm against the byte one packet further on, when it has arrived.
            // When it has not, the candidate is taken on trust and checked again as
            // the next packet is read — waiting for confirmation would stall a
            // stream that delivers exactly one packet per read.
            var next = offset + PacketLength;
            if (next < buffer.Count && buffer[next] is not SyncByte)
            {
                continue;
            }

            buffer.RemoveRange(0, offset);
            DiscardedBytes += offset;
            aligned = true;
            return true;
        }

        return false;
    }

    private TsPacket ReadHeader()
    {
        var pid = ((buffer[1] & 0x1F) << 8) | buffer[2];
        var adaptationField = (buffer[3] >> 4) & 0x03;

        return new TsPacket(
            pid,
            buffer[3] & 0x0F,
            HasPayload: adaptationField is 0x01 or 0x03
        );
    }
}
