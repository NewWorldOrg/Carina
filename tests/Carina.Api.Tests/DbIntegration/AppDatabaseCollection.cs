namespace Carina.Api.Tests.DbIntegration;

[CollectionDefinition(Name)]
public sealed class AppDatabaseCollection : ICollectionFixture<AppDatabase>
{
    public const string Name = "carina application database";
}
