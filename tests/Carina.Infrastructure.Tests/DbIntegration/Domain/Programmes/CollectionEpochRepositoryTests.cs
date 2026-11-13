using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Programmes;

[Trait("Category", "DbIntegration")]
public sealed class CollectionEpochRepositoryTests(MigratedRepositoryDatabase database)
    : IClassFixture<MigratedRepositoryDatabase>, IAsyncLifetime
{
    private static readonly DateTime At = new(2026, 8, 18, 9, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact]
    public async Task TheFirstReadBeginsTheEpochAndKeepsIt()
    {
        await ForgetTheEpochAsync();

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        CollectionEpoch begun = await EpochsIn(scope).ReadAsync(At, Cancel);

        Assert.Equal(1, begun.Generation);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        CollectionEpoch read = await EpochsIn(reading).ReadAsync(At, Cancel);

        Assert.Equal(1, read.Generation);
        Assert.Equal(At, read.AdvancedAt);
    }

    [Fact]
    public async Task AnAdvancedEpochIsStillAdvancedWhenItIsReadBack()
    {
        await ForgetTheEpochAsync();

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        ICollectionEpochRepository repository = EpochsIn(scope);
        CollectionEpoch epoch = await repository.ReadAsync(At, Cancel);

        epoch.Advance(At.AddHours(1));

        await repository.SaveAsync(epoch, Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        CollectionEpoch read = await EpochsIn(reading).ReadAsync(At, Cancel);

        Assert.Equal(2, read.Generation);
        Assert.Equal(At.AddHours(1), read.AdvancedAt);
    }

    private async Task ForgetTheEpochAsync()
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await ContextIn(scope).Set<CollectionEpoch>().ExecuteDeleteAsync(Cancel);
    }

    private static ICollectionEpochRepository EpochsIn(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ICollectionEpochRepository>();

    private static CarinaDbContext ContextIn(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
}
