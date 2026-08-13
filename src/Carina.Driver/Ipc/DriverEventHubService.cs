using Carina.Driver.Events;
using Carina.Driver.Sessions;

using Microsoft.Extensions.Hosting;

namespace Carina.Driver.Ipc;

public sealed class DriverEventHubService(DriverEventHub hub, TunerSessionManager manager)
    : IHostedLifecycleService
{
    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        manager.EnterDraining();
        hub.CloseAll();
        manager.DetachEverySubscriber();

        return Task.CompletedTask;
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
