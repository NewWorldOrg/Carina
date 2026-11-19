namespace Carina.Domain.DataBroadcast;

public readonly record struct ModuleVersionKey(int Tag, uint DownloadId, int ModuleId, int Version);
