using Carina.Domain.Migration;
using Carina.Infrastructure.Migration;
using Carina.Infrastructure.Persistence;

namespace Carina.Infrastructure.Tests.Migration;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class MigrationLeaseTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ASecondRunIsRefusedWhileTheFirstStillHoldsTheLease()
    {
        await using CarinaDbContext first = database.Open();
        await using CarinaDbContext second = database.Open();

        IAsyncDisposable? held = await new MigrationLease(first).TakeAsync(Cancel);

        Assert.NotNull(held);
        Assert.Null(await new MigrationLease(second).TakeAsync(Cancel));

        await held.DisposeAsync();
    }

    [Fact]
    public async Task TheNextRunGetsTheLeaseOnceTheFirstHasGivenItBack()
    {
        await using CarinaDbContext first = database.Open();
        await using CarinaDbContext second = database.Open();

        IAsyncDisposable? held = await new MigrationLease(first).TakeAsync(Cancel);
        Assert.NotNull(held);
        await held.DisposeAsync();

        IAsyncDisposable? next = await new MigrationLease(second).TakeAsync(Cancel);

        Assert.NotNull(next);

        await next.DisposeAsync();
    }
}
