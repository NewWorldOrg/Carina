using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

public sealed class TestingWebApplicationFactoryTests
{
    [Fact(DisplayName = "an application that fails to stop does not keep the others raised from the same host from being stopped")]
    public async Task AnApplicationThatFailsToStopDoesNotKeepTheOthersFromBeingStopped()
    {
        StopCount stops = new();
        TestingWebApplicationFactory root = new();
        WebApplicationFactory<Program> failing = Reshaped(root, new RefusesToStop());
        WebApplicationFactory<Program> counted = Reshaped(root, new CountsStops(stops));
        _ = failing.Services;
        _ = counted.Services;

        await Assert.ThrowsAsync<AggregateException>(() => root.DisposeAsync().AsTask());

        Assert.True(stops.Count > 0, "an application raised after the one that refused to stop was never stopped");
    }

    [Fact(DisplayName = "an application raised from a host reshaped twice is stopped with the root")]
    public async Task AnApplicationRaisedFromAHostReshapedTwiceIsStoppedWithTheRoot()
    {
        StopCount stops = new();
        TestingWebApplicationFactory root = new();
        WebApplicationFactory<Program> twice = Reshaped(Reshaped(root, new CountsStops(new StopCount())), new CountsStops(stops));
        _ = twice.Services;

        await root.DisposeAsync();

        Assert.True(stops.Count > 0, "the application of a host reshaped twice was never stopped");
    }

    [Fact(DisplayName = "an application a reshaped host already let go of is not stopped again with the root")]
    public async Task AnApplicationAReshapedHostAlreadyLetGoOfIsNotStoppedAgain()
    {
        StopCount stops = new();
        TestingWebApplicationFactory root = new();
        WebApplicationFactory<Program> counted = Reshaped(root, new CountsStops(stops));
        _ = counted.Services;

        await counted.DisposeAsync();
        int stoppedWithItsOwnHost = stops.Count;
        await root.DisposeAsync();

        Assert.Equal(stoppedWithItsOwnHost, stops.Count);
    }

    private static WebApplicationFactory<Program> Reshaped(WebApplicationFactory<Program> from, IHostedService watching)
        => from.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddSingleton(watching)));

    private sealed class StopCount
    {
        private int count;

        public int Count => count;

        public void Add() => Interlocked.Increment(ref count);
    }

    private sealed class CountsStops(StopCount stops) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            stops.Add();

            return Task.CompletedTask;
        }
    }

    private sealed class RefusesToStop : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("this application refuses to stop");
    }
}
