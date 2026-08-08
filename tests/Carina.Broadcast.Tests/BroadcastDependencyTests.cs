namespace Carina.Broadcast.Tests;

public sealed class BroadcastDependencyTests
{
    // Complements the project-file rule in Carina.Architecture.Tests: even the
    // compiled assembly must not pull anything in besides the base class library,
    // so the parsing code stays testable against fixed fixtures alone.
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
