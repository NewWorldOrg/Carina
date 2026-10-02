namespace Carina.Infrastructure.Tests;

/// <summary>
/// The tests that rewrite this process's environment. They run with nothing beside them, so no
/// other test starts a programme while the environment is not the one it was given.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "this process's environment";
}
