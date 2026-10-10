namespace Carina.Domain.DataBroadcast;

/// <summary>
/// What tells one module version of a recording's data broadcast from every other: the carousel's tag, the
/// download it came in, the module and its version.
/// </summary>
public readonly record struct ModuleVersionKey(int Tag, uint DownloadId, int ModuleId, int Version);
