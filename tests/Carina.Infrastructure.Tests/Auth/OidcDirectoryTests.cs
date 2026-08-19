using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Auth;

public sealed class OidcDirectoryTests
{
    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AProviderThatAnswersIsFetchedOnceAndHeldForTheRestOfItsWindow()
    {
        await using var harness = new Harness();

        Assert.NotNull(await harness.Directory.ForAsync(Configured(), default));
        Assert.NotNull(await harness.Directory.ForAsync(Configured(), default));

        Assert.Single(harness.Idp.Visits);
        Assert.Equal(OidcReach.Reachable, harness.Reachability.State);
    }

    [Fact]
    public async Task OnceTheWindowHasPassedTheDocumentIsReadAgain()
    {
        await using var harness = new Harness();

        Assert.NotNull(await harness.Directory.ForAsync(Configured(), default));
        harness.Clock.Wind(OidcLoginPolicy.Default.DirectoryLifetime);
        Assert.NotNull(await harness.Directory.ForAsync(Configured(), default));

        Assert.Equal(2, harness.Idp.Visits.Count);
    }

    [Fact]
    public async Task AProviderOutOfReachIsRecordedAsDegradedRatherThanThrown()
    {
        await using var harness = new Harness();
        harness.Idp.Reachable = false;

        Assert.Null(await harness.Directory.ForAsync(Configured(), default));
        Assert.Equal(OidcReach.OutOfReach, harness.Reachability.State);
    }

    [Fact]
    public async Task AnInstallationWithNoProviderIsNotDegradedBecauseThereIsNothingToReach()
    {
        await using var harness = new Harness();
        harness.Reachability.Record(OidcReach.OutOfReach);

        Assert.Null(await harness.Directory.ForAsync(OidcSettings.Unconfigured(At), default));
        Assert.Equal(OidcReach.NotConfigured, harness.Reachability.State);
        Assert.Empty(harness.Idp.Visits);
    }

    [Fact]
    public async Task TheProbeSaysNothingIsConfiguredWhereNothingIs()
    {
        await using var harness = new Harness();
        var settings = new HeldOidcSettings();

        await harness.Probe.ProbeOnceAsync(settings, harness.Directory, default);

        Assert.Equal(OidcReach.NotConfigured, harness.Reachability.State);
    }

    [Fact]
    public async Task TheProbeSurfacesAProviderThatDoesNotAnswerWithoutAnybodyTryingToSignIn()
    {
        await using var harness = new Harness();
        harness.Idp.Reachable = false;
        var settings = new HeldOidcSettings { Settings = Configured() };

        await harness.Probe.ProbeOnceAsync(settings, harness.Directory, default);

        Assert.Equal(OidcReach.OutOfReach, harness.Reachability.State);
    }

    [Fact]
    public async Task TheProbeClearsTheDegradedStateOnceTheProviderAnswersAgain()
    {
        await using var harness = new Harness();
        var settings = new HeldOidcSettings { Settings = Configured() };

        harness.Idp.Reachable = false;
        await harness.Probe.ProbeOnceAsync(settings, harness.Directory, default);
        harness.Idp.Reachable = true;
        await harness.Probe.ProbeOnceAsync(settings, harness.Directory, default);

        Assert.Equal(OidcReach.Reachable, harness.Reachability.State);
    }

    private static OidcSettings Configured()
        => OidcSettings.Rehydrate(
            OidcSettings.TheOnlyRow,
            MockIdentityProvider.DiscoveryUrl,
            "carina",
            new ClientSecret("the-client-secret"),
            At);

    private sealed class Harness : IAsyncDisposable
    {
        private readonly HttpClient client;

        public Harness()
        {
            client = new HttpClient(Idp);
            Directory = new OidcDirectory(
                new OidcGateway(client),
                new OidcDirectoryCache(),
                Reachability,
                OidcLoginPolicy.Default,
                Clock);
            Probe = new OidcDiscoveryProbe(
                new ThrowingScopes(),
                Reachability,
                Clock,
                NullLogger<OidcDiscoveryProbe>.Instance);
        }

        public MockIdentityProvider Idp { get; } = new();

        public WoundClock Clock { get; } = new(At);

        public OidcReachability Reachability { get; } = new();

        public OidcDirectory Directory { get; }

        public OidcDiscoveryProbe Probe { get; }

        public ValueTask DisposeAsync()
        {
            Probe.Dispose();
            client.Dispose();
            Idp.Dispose();

            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingScopes : Microsoft.Extensions.DependencyInjection.IServiceScopeFactory
    {
        public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope()
            => throw new InvalidOperationException(
                "The probe is driven straight through ProbeOnceAsync here, so it never asks for a scope.");
    }
}
