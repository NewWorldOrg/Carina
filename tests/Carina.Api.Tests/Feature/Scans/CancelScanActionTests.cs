using System.Net;
using System.Text.Json;

using Carina.Api.Common;
using Carina.Domain.Channels;
using Carina.Domain.Scans;

namespace Carina.Api.Tests.Feature.Scans;

[Trait("Category", "Feature")]
public sealed class CancelScanActionTests
{
    private static readonly DateTime At = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AScanNamedByTheUuidThatIsAllZeroesIsRefusedAsARequestAndNotAFault()
    {
        await using var feature = new ScanFeature();
        string path = $"/api/tuners/scan/{Guid.Empty}/cancel";

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(ScanIdText.Description, body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task CancellingAWalkingScanLeavesTheExistingDefinitionsAlone()
    {
        await using var feature = new ScanFeature { Orchestrator = { HoldsOpen = true } };

        feature.Services.Services.Add(BroadcastService.Discover(
            new NetworkId(1),
            new ServiceId(101),
            "Untouched",
            ServiceCategory.Television,
            At));

        Guid scanId = await feature.StartAsync();
        (HttpStatusCode status, JsonElement _) = await feature.PostAsync($"/api/tuners/scan/{scanId}/cancel");

        await feature.UntilSettled(scanId);

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(ScanRunState.Cancelled, feature.Runs.Runs[0].State);
        Assert.Single(feature.Services.Services);
    }

    [Fact]
    public async Task CancellingAScanThatHasAlreadyEndedIsRefused()
    {
        await using var feature = new ScanFeature();

        Guid scanId = await feature.StartAsync();
        await feature.UntilSettled(scanId);

        (HttpStatusCode status, JsonElement _) = await feature.PostAsync($"/api/tuners/scan/{scanId}/cancel");

        Assert.Equal(HttpStatusCode.Conflict, status);
    }
}
