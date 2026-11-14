using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Db.Tests.DbIntegration.Infra.Persistence;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class PersistenceSchemaTests
{
    [Fact]
    public async Task AppliesEveryMigrationToAnEmptyDatabase()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(MigrationScratch.ConnectionString());
        await context.Database.EnsureDeletedAsync();

        await context.Database.MigrateAsync();

        string[] applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.EndsWith("_Initial", applied[0], StringComparison.Ordinal);
        Assert.Equal(context.Database.GetMigrations(), applied);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task ReRunningTheMigrationsIsIdempotent()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(MigrationScratch.ConnectionString());
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        string[] firstRun = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

        await context.Database.MigrateAsync();

        Assert.Equal(firstRun, await context.Database.GetAppliedMigrationsAsync());
    }
}
