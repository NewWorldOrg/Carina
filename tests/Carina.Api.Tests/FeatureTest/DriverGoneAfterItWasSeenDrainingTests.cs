using System.Diagnostics;
using System.Runtime.Versioning;

using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

[SupportedOSPlatform("linux")]
public sealed class DriverGoneAfterItWasSeenDrainingTests
{
    private static readonly TimeSpan LongEnoughToAskAgain = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Holds the driver between closing its event feeds and closing its socket, which is where a busy
    /// machine leaves it for a moment on every stop.
    /// </summary>
    private sealed class SlowToStop : IHostedLifecycleService
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => released.TrySetResult();

        public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => released.Task;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact(DisplayName = "a driver the app asked while it was on its way down, and heard was draining, is reported gone soon after it is gone")]
    public async Task ADriverHeardDrainingOnItsWayDownIsReportedGoneSoonAfterItIsGone()
    {
        var slow = new SlowToStop();

        await using AppSwapFeature feature = await AppSwapFeature.StartAsync(
            reshapeDriver: services => services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(slow)));

        Task stopping = feature.Driver.BeginStop();

        try
        {
            await feature.App.UntilConnectionIs("draining");
            await Task.Delay(LongEnoughToAskAgain);
            await feature.App.UntilConnectionIs("draining");
        }
        finally
        {
            slow.Release();
        }

        await stopping;
        await feature.Driver.PutDownAsync();

        Stopwatch waited = Stopwatch.StartNew();

        await feature.App.UntilConnectionIs("notConnected");

        Assert.True(
            waited.Elapsed < Eventually.Patience / 3,
            $"The app took {waited.Elapsed} to notice the driver was gone, which leaves a test that waits "
            + $"{Eventually.Patience} for it no room on a busy machine.");
    }
}
