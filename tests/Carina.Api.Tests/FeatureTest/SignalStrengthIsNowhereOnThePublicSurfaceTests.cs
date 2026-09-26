using System.Text.Json.Nodes;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class SignalStrengthIsNowhereOnThePublicSurfaceTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Fact(DisplayName = "nothing the application describes names a signal strength")]
    public async Task NothingTheApplicationDescribesNamesASignalStrength()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        string served = document.ToJsonString();

        Assert.DoesNotContain("signalStrength", served, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signal_strength", served, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "the statistics this system does take are named in the same document")]
    public async Task TheStatisticsThisSystemDoesTakeAreNamedInTheSameDocument()
    {
        JsonNode document = await ServedOpenApi.FetchAsync(factory);

        string served = document.ToJsonString();

        Assert.Contains("carrierToNoise", served, StringComparison.Ordinal);
        Assert.Contains("bitErrorRate", served, StringComparison.Ordinal);
        Assert.Contains("lockRate", served, StringComparison.Ordinal);
    }
}
