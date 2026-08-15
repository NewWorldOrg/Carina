using System.Net;
using System.Text.Json;

using Carina.Contracts;
using Carina.TestSupport;

using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class DriverShutdownEndpointTests
{
    private static readonly Uri Shutdown = new("/api/driver/shutdown", UriKind.Relative);

    private static readonly DateTimeOffset Accepted =
        new(2026, 8, 15, 22, 30, 0, TimeSpan.FromHours(9));

    private static DriverHello Capable(string[]? capabilities = null)
        => FakeDriver.HelloFor(
            "instance-a",
            capabilities: capabilities ?? [DriverCapabilities.GracefulShutdown]);

    private static void Willing(FakeDriver driver)
        => driver.Shutdown = new DriverShutdownDto
        {
            InstanceId = "instance-a",
            AcceptedAt = Accepted,
            BudgetSeconds = 30,
        };

    private static void Recording(FakeDriver driver)
    {
        Willing(driver);

        driver.RefusalsByPath[DriverEndpoints.Shutdown] = new FakeDriver.Refusal(
            StatusCodes.Status409Conflict,
            new DriverProblem(
                "recordingInProgress",
                ["1 recording is running; the last one ends at 2026-08-15T23:00:00.0000000+09:00."]));
    }

    private static void MissingTheEndpoint(FakeDriver driver)
    {
        Willing(driver);

        driver.RefusalsByPath[DriverEndpoints.Shutdown] = new FakeDriver.Refusal(
            StatusCodes.Status404NotFound,
            null);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return (response.StatusCode, document.RootElement.Clone());
    }

    [Fact]
    public async Task AskingForAShutdownReachesTheDriverAndCarriesBackWhatItAnswered()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), Willing);

        using var response = await feature.Client.PostAsync(Shutdown, null);
        var (status, body) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(1, feature.Driver.RequestsFor(DriverEndpoints.Shutdown));

        var data = body.GetProperty("data");

        Assert.Equal("instance-a", data.GetProperty("instanceId").GetString());
        Assert.Equal(Accepted, data.GetProperty("acceptedAt").GetDateTimeOffset());
        Assert.Equal(30, data.GetProperty("budgetSeconds").GetInt32());
    }

    [Fact]
    public async Task AShutdownIsRefusedWhileTheDriverIsRecordingAndTheScreenIsToldWhy()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), Recording);

        using var response = await feature.Client.PostAsync(Shutdown, null);
        var (status, body) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Contains(
            "recordingInProgress",
            body.GetProperty("message").GetString()!,
            StringComparison.Ordinal);
        Assert.Contains(
            "ends at",
            body.GetProperty("message").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADriverThatIsNotThereIsAnsweredAsUnavailableRatherThanAsARefusal()
    {
        await using var feature = await DriverFeature.StartAsync();

        using var response = await feature.Client.PostAsync(Shutdown, null);
        var (status, body) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.NotEmpty(body.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task ADriverTooOldToStopOnRequestIsAnsweredAsUnimplementedRatherThanBroken()
    {
        await using var feature = await DriverFeature.StartAsync(
            Capable([DriverCapabilities.Recording]),
            Willing);

        using var response = await feature.Client.PostAsync(Shutdown, null);
        var (status, body) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.NotImplemented, status);
        Assert.Equal(0, feature.Driver.RequestsFor(DriverEndpoints.Shutdown));
        Assert.Contains(
            DriverCapabilities.GracefulShutdown,
            body.GetProperty("message").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADriverThatPromisesTheEndpointAndThenHasNoneIsBlamedRatherThanTheCaller()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), MissingTheEndpoint);

        using var response = await feature.Client.PostAsync(Shutdown, null);
        var (status, body) = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Contains(
            "not the same build",
            body.GetProperty("message").GetString()!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheShutdownSurfaceIsBehindTheSameDenialAsTheRestOnceASchemeIsRegistered()
    {
        await using var feature = await DriverFeature.StartAsync(Capable(), Willing);
        using var app = new TestingWebApplicationFactory();
        using var client = app.WithTestScheme().CreateClient();

        using var response = await client.PostAsync(Shutdown, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
