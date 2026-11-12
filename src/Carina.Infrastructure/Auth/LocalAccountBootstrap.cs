using Carina.Domain.Auth;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Auth;

public sealed class LocalAccountBootstrap(
    IServiceScopeFactory scopes,
    PasswordHashPolicy policy,
    ILogger<LocalAccountBootstrap> logger) : BackgroundService
{
    public async Task EnsureAnAccountExistsAsync(
        LocalAccountProvisioningDomainService provisioning,
        ILocalAccountRepository accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provisioning);
        ArgumentNullException.ThrowIfNull(accounts);

        if (await provisioning.FirstAccountAsync(policy, cancellationToken) is not { } made)
        {
            return;
        }

        await accounts.SaveAsync(made.Account, cancellationToken);

        logger.LogWarning(
            "No local account existed, so one was made. Sign in as {Username} with {Password} and change it: "
            + "this is the only time the password is written anywhere.",
            made.Account.Username,
            made.Password);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();

            await EnsureAnAccountExistsAsync(
                scope.ServiceProvider.GetRequiredService<LocalAccountProvisioningDomainService>(),
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
