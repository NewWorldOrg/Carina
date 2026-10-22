using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Carina.Domain.Base;
using Carina.Domain.Segments;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class SegmentSettingsFeature : IAsyncDisposable
{
    public const string Path = "/api/segments/settings";

    public static readonly DateTime Noon = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public SegmentSettingsFeature()
    {
        Built = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ISegmentSettingsRepository>(Settings);
            services.AddSingleton<IAtomicWrite, UnguardedWrites>();
        }));

        Client = Built.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "anything");
    }

    public WebApplicationFactory<Program> Built { get; }

    public HttpClient Client { get; }

    public MovingClock Clock { get; } = new(Noon);

    public HeldSegmentSettings Settings { get; } = new();

    public HttpClient Anonymous() => Built.WithTestScheme().CreateClient();

    public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync()
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri(Path, UriKind.Relative));

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> PatchAsync(object body)
    {
        using HttpResponseMessage response = await Client.PatchAsJsonAsync(new Uri(Path, UriKind.Relative), body);

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> PatchAsync(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Client.PatchAsync(new Uri(Path, UriKind.Relative), content);

        return await ReadAsync(response);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        if (!body.StartsWith('{'))
        {
            return (response.StatusCode, default);
        }

        using var document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.Clone());
    }
}

public sealed class SegmentSettingsEndpointTests
{
    [Fact(DisplayName = "with no row held the threshold reads at 0.70, and nobody has changed it")]
    public async Task WithNoRowHeldTheThresholdReadsAtItsDefault()
    {
        await using var feature = new SegmentSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync();
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["skipThreshold", "skipThresholdChangedAt"], data.EnumerateObject().Select(field => field.Name).ToArray());
        Assert.Equal(0.70, data.GetProperty("skipThreshold").GetDouble());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("skipThresholdChangedAt").ValueKind);
        Assert.Equal(0, feature.Settings.Saves);
    }

    [Fact(DisplayName = "BR-CV-002: changing the threshold on no row answers when it changed, and is read back")]
    public async Task ChangingTheThresholdOnNoRowIsReadBack()
    {
        await using var feature = new SegmentSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(new { skipThreshold = 0.65 });
        (_, JsonElement read) = await feature.GetAsync();
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0.65, data.GetProperty("skipThreshold").GetDouble());
        Assert.Equal(SegmentSettingsFeature.Noon, data.GetProperty("skipThresholdChangedAt").GetDateTime());
        Assert.Equal(0.65, read.GetProperty("data").GetProperty("skipThreshold").GetDouble());
        Assert.Equal(1, feature.Settings.Saves);
        Assert.Equal(1, feature.Settings.Turns);
    }

    [Theory(DisplayName = "BR-CV-002: a threshold is taken to the hundredth")]
    [InlineData(0.654, 0.65)]
    [InlineData(0.655, 0.66)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.95, 0.95)]
    public async Task AThresholdIsTakenToTheHundredth(double sent, double taken)
    {
        await using var feature = new SegmentSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(new { skipThreshold = sent });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(taken, body.GetProperty("data").GetProperty("skipThreshold").GetDouble());
    }

    [Theory(DisplayName = "BR-CV-002: a threshold outside 0.50 to 0.95 is refused naming it, and nothing is written")]
    [InlineData("""{"skipThreshold":0.49}""")]
    [InlineData("""{"skipThreshold":0.4999}""")]
    [InlineData("""{"skipThreshold":0.96}""")]
    [InlineData("""{"skipThreshold":0.9501}""")]
    [InlineData("""{"skipThreshold":1}""")]
    [InlineData("""{"skipThreshold":-0.7}""")]
    public async Task AThresholdOutsideTheRangeIsRefused(string json)
    {
        await using var feature = new SegmentSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("skipThreshold", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.Equal(0, feature.Settings.Saves);
        Assert.Equal(0, feature.Settings.Turns);
    }

    [Fact(DisplayName = "BR-CV-002: sending the threshold that stands writes nothing and keeps when it last changed")]
    public async Task SendingTheThresholdThatStandsWritesNothing()
    {
        await using var feature = new SegmentSettingsFeature();
        (HttpStatusCode untouched, JsonElement unset) = await feature.PatchAsync(new { skipThreshold = 0.70 });
        feature.Clock.Now = SegmentSettingsFeature.Noon.AddMinutes(5);
        await feature.PatchAsync(new { skipThreshold = 0.8 });
        feature.Clock.Now = SegmentSettingsFeature.Noon.AddMinutes(10);

        (HttpStatusCode again, JsonElement body) = await feature.PatchAsync(new { skipThreshold = 0.8 });
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, untouched);
        Assert.Equal(JsonValueKind.Null, unset.GetProperty("data").GetProperty("skipThresholdChangedAt").ValueKind);
        Assert.Equal(HttpStatusCode.OK, again);
        Assert.Equal(0.8, data.GetProperty("skipThreshold").GetDouble());
        Assert.Equal(SegmentSettingsFeature.Noon.AddMinutes(5), data.GetProperty("skipThresholdChangedAt").GetDateTime());
        Assert.Equal(1, feature.Settings.Saves);
        Assert.Equal(3, feature.Settings.Turns);
    }

    [Theory(DisplayName = "a change that names no threshold is refused naming it")]
    [InlineData("{}")]
    [InlineData("""{"skipThreshold":null}""")]
    [InlineData("null")]
    public async Task AChangeThatNamesNoThresholdIsRefused(string json)
    {
        await using var feature = new SegmentSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("skipThreshold", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(0, feature.Settings.Saves);
        Assert.Equal(0, feature.Settings.Turns);
    }

    [Theory(DisplayName = "a change naming the threshold as anything but a number is refused naming it")]
    [InlineData("""{"skipThreshold":true}""")]
    [InlineData("""{"skipThreshold":[0.7]}""")]
    public async Task AChangeNamingTheThresholdAsAnythingButANumberIsRefused(string json)
    {
        await using var feature = new SegmentSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("skipThreshold", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(0, feature.Settings.Saves);
    }

    [Theory(DisplayName = "BR-CD-013: a change naming a setting this cannot change, learning included, is refused naming it, even beside the threshold")]
    [InlineData("""{"learning":true}""", "learning")]
    [InlineData("""{"learning":false,"skipThreshold":0.8}""", "learning")]
    [InlineData("""{"skipThreshold":0.8,"other":1}""", "other")]
    [InlineData("""{"skipThreshold":0.8,"threshold":0.8}""", "threshold")]
    public async Task AChangeNamingASettingThisCannotChangeIsRefused(string json, string named)
    {
        await using var feature = new SegmentSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(named, body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(0, feature.Settings.Saves);
    }

    [Fact(DisplayName = "a change sent from another origin is refused and writes nothing")]
    public async Task AChangeSentFromAnotherOriginIsRefused()
    {
        await using var feature = new SegmentSettingsFeature();
        feature.Client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        feature.Client.DefaultRequestHeaders.Add(HeaderNames.Origin, "https://elsewhere.example");

        (HttpStatusCode status, _) = await feature.PatchAsync(new { skipThreshold = 0.8 });

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(0, feature.Settings.Saves);
    }

    [Fact(DisplayName = "a change sent as a form is refused and writes nothing")]
    public async Task AChangeSentAsAFormIsRefused()
    {
        await using var feature = new SegmentSettingsFeature();
        using var form = new StringContent("skipThreshold=0.8", Encoding.UTF8, "application/x-www-form-urlencoded");

        using HttpResponseMessage response = await feature.Client.PatchAsync(
            new Uri(SegmentSettingsFeature.Path, UriKind.Relative),
            form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(0, feature.Settings.Saves);
    }

    [Fact(DisplayName = "a caller who has not signed in can neither read nor change the settings")]
    public async Task ACallerWhoHasNotSignedInCanNeitherReadNorChange()
    {
        await using var feature = new SegmentSettingsFeature();
        using HttpClient anonymous = feature.Anonymous();

        using HttpResponseMessage read = await anonymous.GetAsync(new Uri(SegmentSettingsFeature.Path, UriKind.Relative));
        using HttpResponseMessage changed = await anonymous.PatchAsJsonAsync(
            new Uri(SegmentSettingsFeature.Path, UriKind.Relative),
            new { skipThreshold = 0.8 });

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, changed.StatusCode);
        Assert.Equal(0, feature.Settings.Reads);
        Assert.Equal(0, feature.Settings.Saves);
    }
}
