using Carina.Domain.Notifications;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Notifications;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class NotificationRuleRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider services = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await services.DisposeAsync();

    [Fact(DisplayName = "BR-NT-006: a writer of the destinations and the sets of rules holds its turn until its transaction ends, and the next one waits for it")]
    public async Task AWriterOfTheNotificationsHoldsItsTurnUntilItsTransactionEnds()
    {
        await using AsyncServiceScope first = services.CreateAsyncScope();
        await using AsyncServiceScope second = services.CreateAsyncScope();
        await using IDbContextTransaction holding = await Context(first).Database.BeginTransactionAsync(Cancel);

        await Rules(first).TakeTurnAsync(Cancel);

        await using IDbContextTransaction waiting = await Context(second).Database.BeginTransactionAsync(Cancel);
        Task next = Rules(second).TakeTurnAsync(Cancel);

        Assert.NotSame(next, await Task.WhenAny(next, Task.Delay(TimeSpan.FromMilliseconds(300), Cancel)));

        await holding.CommitAsync(Cancel);
        await next.WaitAsync(TimeSpan.FromSeconds(10), Cancel);
        await waiting.CommitAsync(Cancel);
    }

    private static INotificationRuleRepository Rules(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<INotificationRuleRepository>();

    private static CarinaDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
}
