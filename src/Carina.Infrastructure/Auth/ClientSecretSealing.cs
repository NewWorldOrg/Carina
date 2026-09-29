using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Auth;

public sealed class ClientSecretSealing(
    IServiceScopeFactory scopes,
    ILogger<ClientSecretSealing> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            OidcSettingsRepository settings = ActivatorUtilities.CreateInstance<OidcSettingsRepository>(
                scope.ServiceProvider);

            if (await settings.SealAnUnsealedSecretAsync(stoppingToken))
            {
                logger.LogInformation("The identity provider's client secret was held in the clear and is now sealed.");
            }

            if (await settings.FindAsync(stoppingToken) is { SecretLost: true })
            {
                logger.LogWarning(
                    "The identity provider's client secret cannot be opened with the keys in {Setting}, "
                    + "so only the local account signs in until the secret is entered again on the settings screen.",
                    SealingKeyOptions.DirectoryKey);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            logger.LogError(
                failure,
                "The identity provider's client secret could not be sealed; it is sealed on the next start or save.");
        }
    }
}
