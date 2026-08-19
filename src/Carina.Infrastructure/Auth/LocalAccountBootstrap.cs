using Carina.Domain.Auth;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Auth;

public sealed class LocalAccountBootstrap(
    IServiceScopeFactory scopes,
    IPasswordHasher hasher,
    PasswordHashPolicy policy,
    TimeProvider clock,
    ILogger<LocalAccountBootstrap> logger) : BackgroundService
{
    public async Task EnsureAnAccountExistsAsync(
        ILocalAccountRepository accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        if (await accounts.FindAsync(cancellationToken) is not null)
        {
            return;
        }

        string password = FirstCredentials.MakePassword();
        LocalAccount account = LocalAccount.Bootstrap(
            FirstCredentials.Username,
            hasher.Hash(password, policy),
            clock.GetUtcNow().UtcDateTime);

        await accounts.SaveAsync(account, cancellationToken);

        logger.LogWarning(
            "No local account existed, so one was made. Sign in as {Username} with {Password} and change it: "
            + "this is the only time the password is written anywhere.",
            account.Username,
            password);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();

            await EnsureAnAccountExistsAsync(
                scope.ServiceProvider.GetRequiredService<ILocalAccountRepository>(),
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            logger.LogError(
                failure,
                "The local account could not be made, so nobody can sign in until this is fixed.");
        }
    }
}
