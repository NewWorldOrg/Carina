using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Auth;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Infra.Auth;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class LocalAccountBootstrapTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider services = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await services.DisposeAsync();

    [Fact]
    public async Task TheBootstrapFindsTheAccountItAlreadyMadeAndLeavesItAlone()
    {
        var log = new RecordedLog();

        await ClearAsync();

        await using (AsyncServiceScope writing = services.CreateAsyncScope())
        {
            await EnsureAsync(log, writing);
        }

        string first;

        await using (AsyncServiceScope reading = services.CreateAsyncScope())
        {
            first = (await Accounts(reading).FindAsync(Cancel))!.PasswordHash.Value;
        }

        await using (AsyncServiceScope again = services.CreateAsyncScope())
        {
            await EnsureAsync(log, again);
        }

        await using AsyncServiceScope after = services.CreateAsyncScope();
        LocalAccount? read = await Accounts(after).FindAsync(Cancel);

        Assert.Equal(first, read!.PasswordHash.Value);
        Assert.Single(log.Lines);
    }

    private Task EnsureAsync(RecordedLog log, AsyncServiceScope scope)
        => ActivatorUtilities.CreateInstance<LocalAccountBootstrap>(services, log)
            .EnsureAnAccountExistsAsync(
                scope.ServiceProvider.GetRequiredService<LocalAccountProvisioningDomainService>(),
                Accounts(scope),
                Cancel);

    private static ILocalAccountRepository Accounts(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ILocalAccountRepository>();

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();

        await clearing.Set<LocalAccount>().ExecuteDeleteAsync(Cancel);
    }
}
