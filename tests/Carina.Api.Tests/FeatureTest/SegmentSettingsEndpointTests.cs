using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

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
    [Fact(DisplayName = "with no row held learning reads as off, and nobody has changed it")]
    public async Task WithNoRowHeldLearningReadsAsOff()
    {
        await using var feature = new SegmentSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync();
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(data.GetProperty("learning").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("learningChangedAt").ValueKind);
        Assert.Equal(0, feature.Settings.Saves);
    }

    [Fact(DisplayName = "switching learning on writes the row, answers when it changed, and is read back")]
    public async Task SwitchingLearningOnWritesTheRow()
    {
        await using var feature = new SegmentSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(new { learning = true });
        (_, JsonElement read) = await feature.GetAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("data").GetProperty("learning").GetBoolean());
        Assert.Equal(SegmentSettingsFeature.Noon, body.GetProperty("data").GetProperty("learningChangedAt").GetDateTime());
        Assert.True(read.GetProperty("data").GetProperty("learning").GetBoolean());
        Assert.Equal(1, feature.Settings.Saves);
    }

    [Fact(DisplayName = "switching learning off again writes when it changed")]
    public async Task SwitchingLearningOffAgainWritesWhenItChanged()
    {
        await using var feature = new SegmentSettingsFeature();
        await feature.PatchAsync(new { learning = true });
        feature.Clock.Now = SegmentSettingsFeature.Noon.AddMinutes(5);

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(new { learning = false });
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(data.GetProperty("learning").GetBoolean());
        Assert.Equal(SegmentSettingsFeature.Noon.AddMinutes(5), data.GetProperty("learningChangedAt").GetDateTime());
        Assert.Equal(2, feature.Settings.Saves);
    }

    [Fact(DisplayName = "sending what learning already is changes nothing, and keeps when it last changed")]
    public async Task SendingWhatLearningAlreadyIsChangesNothing()
    {
        await using var feature = new SegmentSettingsFeature();
        await using var untouched = new SegmentSettingsFeature();
        await feature.PatchAsync(new { learning = true });
        feature.Clock.Now = SegmentSettingsFeature.Noon.AddMinutes(5);

        (HttpStatusCode again, JsonElement body) = await feature.PatchAsync(new { learning = true });
        (HttpStatusCode never, JsonElement off) = await untouched.PatchAsync(new { learning = false });

        Assert.Equal(HttpStatusCode.OK, again);
        Assert.Equal(SegmentSettingsFeature.Noon, body.GetProperty("data").GetProperty("learningChangedAt").GetDateTime());
        Assert.Equal(1, feature.Settings.Saves);
        Assert.Equal(HttpStatusCode.OK, never);
        Assert.False(off.GetProperty("data").GetProperty("learning").GetBoolean());
        Assert.Equal(JsonValueKind.Null, off.GetProperty("data").GetProperty("learningChangedAt").ValueKind);
        Assert.Equal(0, untouched.Settings.Saves);
    }

    [Theory(DisplayName = "a change that names nothing, or names learning as anything but true or false, is refused")]
    [InlineData("{}")]
    [InlineData("""{"learning":null}""")]
    [InlineData("""{"learning":"true"}""")]
    [InlineData("""{"learning":1}""")]
    [InlineData("""{"skipThreshold":0.8}""")]
    [InlineData("null")]
    public async Task AChangeThatNamesNothingOrNamesLearningWronglyIsRefused(string json)
    {
        await using var feature = new SegmentSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("learning", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(0, feature.Settings.Saves);
    }

    [Fact(DisplayName = "a change sent from another origin is refused and writes nothing")]
    public async Task AChangeSentFromAnotherOriginIsRefused()
    {
        await using var feature = new SegmentSettingsFeature();
        feature.Client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        feature.Client.DefaultRequestHeaders.Add(HeaderNames.Origin, "https://elsewhere.example");

        (HttpStatusCode status, _) = await feature.PatchAsync(new { learning = true });

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(0, feature.Settings.Saves);
    }

    [Fact(DisplayName = "a change sent as a form is refused and writes nothing")]
    public async Task AChangeSentAsAFormIsRefused()
    {
        await using var feature = new SegmentSettingsFeature();
        using var form = new StringContent("learning=true", Encoding.UTF8, "application/x-www-form-urlencoded");

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
            new { learning = true });

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, changed.StatusCode);
        Assert.Equal(0, feature.Settings.Reads);
        Assert.Equal(0, feature.Settings.Saves);
    }
}
