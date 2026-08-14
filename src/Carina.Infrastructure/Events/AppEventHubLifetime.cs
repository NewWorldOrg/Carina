using Microsoft.Extensions.Hosting;

namespace Carina.Infrastructure.Events;

public sealed class AppEventHubLifetime(AppEventHub hub) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        hub.CloseAll();

        return Task.CompletedTask;
    }
}
