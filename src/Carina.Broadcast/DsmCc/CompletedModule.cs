namespace Carina.Broadcast.DsmCc;

public sealed class CompletedModule
{
    internal CompletedModule(int moduleId, int moduleVersion, string? name, IReadOnlyList<ModuleResource> resources)
    {
        ModuleId = moduleId;
        ModuleVersion = moduleVersion;
        Name = name;
        Resources = resources;
    }

    public int ModuleId { get; }

    public int ModuleVersion { get; }

    public string? Name { get; }

    public IReadOnlyList<ModuleResource> Resources { get; }
}
