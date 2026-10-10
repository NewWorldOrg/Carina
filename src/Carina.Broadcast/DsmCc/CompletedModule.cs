namespace Carina.Broadcast.DsmCc;

public sealed record CompletedModule(int ModuleId, int ModuleVersion, string? Name, IReadOnlyList<ModuleResource> Resources);
