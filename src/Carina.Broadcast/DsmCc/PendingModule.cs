namespace Carina.Broadcast.DsmCc;

internal sealed class PendingModule
{
    private readonly byte[] bytes;
    private readonly bool[] held;

    private int heldCount;

    public PendingModule(HeldModule module, long blockCount)
    {
        Held = module;
        bytes = new byte[module.ModuleSize];
        held = new bool[blockCount];
    }

    public HeldModule Held { get; }

    public bool IsComplete => heldCount == held.Length;

    public ReadOnlyMemory<byte> Bytes => bytes;

    public bool Place(int blockNumber, ReadOnlySpan<byte> data, int blockSize)
    {
        int offset = blockNumber * blockSize;

        if (blockNumber >= held.Length || held[blockNumber] || offset + data.Length > bytes.Length)
        {
            return false;
        }

        data.CopyTo(bytes.AsSpan(offset));
        held[blockNumber] = true;
        heldCount++;

        return true;
    }
}
