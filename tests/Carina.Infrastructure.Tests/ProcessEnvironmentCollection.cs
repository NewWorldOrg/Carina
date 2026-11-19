namespace Carina.Infrastructure.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "this process's environment";
}
