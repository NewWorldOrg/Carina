using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Api.Authentication;

public sealed class PublicOriginDiagnosis(
    PublicOrigin origin,
    ILogger<PublicOriginDiagnosis> logger) : IHostedService
{
    public const string NothingIsNamed =
        "{Key} names nothing, so the redirect URI is guessed from the address each request arrived on. A screen "
        + "rendered from inside this network guesses an address no browser reaches, and the identity provider "
        + "refuses a sign-in the guess was registered for. Name the address browsers use to be rid of this.";

    public const string TheOriginIsNamed =
        "The identity provider is sent {RedirectUri}, and the settings screen asks for that same one to be "
        + "registered, however the request reached this process.";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (origin.IsGuessed)
        {
            logger.LogWarning(NothingIsNamed, PublicOrigin.Key);
        }
        else
        {
            logger.LogInformation(TheOriginIsNamed, PublicOrigin.RedirectUriAt(origin.ToString()));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
