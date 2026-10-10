namespace Carina.Broadcast.DsmCc;

internal sealed class PendingModule
{
    private readonly byte[] bytes;
    private readonly bool[] held;

    private int heldCount;

    public PendingModule(int moduleVersion, long moduleSize, int blockCount)
    {
        ModuleVersion = moduleVersion;
        bytes = new byte[moduleSize];
        held = new bool[blockCount];
    }

    public int ModuleVersion { get; }

    public bool IsComplete => heldCount == held.Length;

    public ReadOnlyMemory<byte> Bytes => bytes;

    public bool Place(int blockNumber, ReadOnlySpan<byte> data, int blockSize)
    {
        if (held[blockNumber])
        {
            return false;
        }

        data.CopyTo(bytes.AsSpan(blockNumber * blockSize));
        held[blockNumber] = true;
        heldCount++;

        return true;
    }
}
