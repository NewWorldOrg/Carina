namespace Carina.Broadcast.DsmCc;

public sealed class DataCarouselCatalogue
{
    internal DataCarouselCatalogue(
        uint downloadId,
        uint transactionId,
        int blockSize,
        IReadOnlyList<ModuleInfo> modules,
        IReadOnlyList<int> withdrawn,
        IReadOnlyList<int> superseded)
    {
        DownloadId = downloadId;
        TransactionId = transactionId;
        BlockSize = blockSize;
        Modules = modules;
        Withdrawn = withdrawn;
        Superseded = superseded;
    }

    public uint DownloadId { get; }

    public uint TransactionId { get; }

    public int BlockSize { get; }

    public IReadOnlyList<ModuleInfo> Modules { get; }

    public IReadOnlyList<int> Withdrawn { get; }

    public IReadOnlyList<int> Superseded { get; }
}
