namespace Carina.Broadcast.Tests;

public sealed class BroadcastDependencyTests
{
    [Fact]
    public void AssemblyReferencesTheBaseClassLibraryOnly()
    {
        var referenced = typeof(BroadcastAssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Where(name => name is not null && !name.StartsWith("System.", StringComparison.Ordinal))
            .Where(name => name is not "System" and not "netstandard" and not "mscorlib")
            .ToArray();

        Assert.Empty(referenced);
    }
}
