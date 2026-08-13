using Carina.Driver.Events;
using Carina.Driver.Sessions;

using Microsoft.Extensions.Hosting;

namespace Carina.Driver;

public sealed class DriverLifecycle(TunerSessionManager manager, DriverEventHub hub)
    : IHostedLifecycleService, IDisposable
{
    private readonly CancellationTokenSource detaching = new();

    public CancellationToken StreamsDetaching => detaching.Token;

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await manager.DrainAsync(cancellationToken);
        }
        finally
        {
            hub.CloseAll();
            manager.DetachEverySubscriber();
            detaching.Cancel();
        }
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => detaching.Dispose();
}
