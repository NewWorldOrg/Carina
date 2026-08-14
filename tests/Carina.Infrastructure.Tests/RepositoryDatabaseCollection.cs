namespace Carina.Infrastructure.Tests;

[CollectionDefinition(Name)]
public sealed class RepositoryDatabaseCollection : ICollectionFixture<RepositoryDatabase>
{
    public const string Name = "carina repository database";
}
