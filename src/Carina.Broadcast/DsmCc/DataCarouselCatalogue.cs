namespace Carina.Broadcast.DsmCc;

public sealed record DataCarouselCatalogue(
    uint DownloadId,
    uint TransactionId,
    int BlockSize,
    IReadOnlyList<ModuleInfo> Modules,
    IReadOnlyList<int> Withdrawn,
    IReadOnlyList<int> Superseded);
