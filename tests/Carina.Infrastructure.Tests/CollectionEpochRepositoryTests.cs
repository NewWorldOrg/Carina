using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class CollectionEpochRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime At = new(2026, 8, 18, 9, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task TheFirstReadBeginsTheEpochAndKeepsIt()
    {
        await using CarinaDbContext context = database.Open();
        CollectionEpoch begun = await new CollectionEpochRepository(context).ReadAsync(At, Cancel);

        Assert.Equal(1, begun.Generation);

        await using CarinaDbContext reading = database.Open();
        CollectionEpoch read = await new CollectionEpochRepository(reading).ReadAsync(At, Cancel);

        Assert.Equal(1, read.Generation);
        Assert.Equal(At, read.AdvancedAt);
    }

    [Fact]
    public async Task AnAdvancedEpochIsStillAdvancedWhenItIsReadBack()
    {
        await using CarinaDbContext context = database.Open();
        var repository = new CollectionEpochRepository(context);
        CollectionEpoch epoch = await repository.ReadAsync(At, Cancel);

        epoch.Advance(At.AddHours(1));

        await repository.SaveAsync(epoch, Cancel);

        await using CarinaDbContext reading = database.Open();
        CollectionEpoch read = await new CollectionEpochRepository(reading).ReadAsync(At, Cancel);

        Assert.Equal(2, read.Generation);
        Assert.Equal(At.AddHours(1), read.AdvancedAt);
    }
}
