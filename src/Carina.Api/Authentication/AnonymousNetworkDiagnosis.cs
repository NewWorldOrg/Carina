using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Api.Authentication;

public sealed class AnonymousNetworkDiagnosis(
    AnonymousNetworks named,
    ILogger<AnonymousNetworkDiagnosis> logger) : IHostedService
{
    public const string TheNetworksAreNamed =
        "{Key} names {Networks}. An address does not stand in for a session anywhere in this process, so a "
        + "request from them is refused like any other carrying none.";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!named.NamesNothing)
        {
            logger.LogWarning(TheNetworksAreNamed, AnonymousNetworks.Key, named.ToString());
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
