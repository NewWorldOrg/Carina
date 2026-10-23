using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Carina.Domain.Base;
using Carina.Domain.DataBroadcast;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class DataBroadcastSettingsFeature : IAsyncDisposable
{
    public const string Path = "/api/data-broadcast/settings";

    public static readonly DateTime Noon = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public DataBroadcastSettingsFeature()
    {
        Built = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IDataBroadcastSettingsRepository>(Settings);
            services.AddSingleton<IAtomicWrite, UnguardedWrites>();
        }));

        Client = Built.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "anything");
    }

    public WebApplicationFactory<Program> Built { get; }

    public HttpClient Client { get; }

    public MovingClock Clock { get; } = new(Noon);

    public HeldDataBroadcastSettings Settings { get; } = new();

    public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync()
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri(Path, UriKind.Relative));

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Client.PutAsync(new Uri(Path, UriKind.Relative), content);

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

public sealed class DataBroadcastSettingsEndpointTests
{
    [Fact(DisplayName = "BR-BD-016: with no row held neither the postal code nor the prefecture is registered, and nobody has changed them")]
    public async Task WithNoRowHeldNothingIsRegistered()
    {
        await using var feature = new DataBroadcastSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync();
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["postalCode", "prefecture", "changedAt"], data.EnumerateObject().Select(field => field.Name).ToArray());
        Assert.All(data.EnumerateObject(), field => Assert.Equal(JsonValueKind.Null, field.Value.ValueKind));
        Assert.Equal(0, feature.Settings.Saves);
    }

    [Fact(DisplayName = "BR-BD-016: registering the postal code and the prefecture answers when they changed, and is read back")]
    public async Task RegisteringIsReadBack()
    {
        await using var feature = new DataBroadcastSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync("""{"postalCode":"0000000","prefecture":13}""");
        (_, JsonElement read) = await feature.GetAsync();
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("0000000", data.GetProperty("postalCode").GetString());
        Assert.Equal(13, data.GetProperty("prefecture").GetInt32());
        Assert.Equal(DataBroadcastSettingsFeature.Noon, data.GetProperty("changedAt").GetDateTime());
        Assert.Equal(data.GetRawText(), read.GetProperty("data").GetRawText());
        Assert.Equal((1, 1), (feature.Settings.Saves, feature.Settings.Turns));
    }

    [Theory(DisplayName = "BR-BD-016: a postal code sent empty or null, and a prefecture sent null, take back what was registered")]
    [InlineData("""{"postalCode":"","prefecture":null}""")]
    [InlineData("""{"postalCode":null,"prefecture":null}""")]
    public async Task EmptyOrNullTakesBackWhatWasRegistered(string json)
    {
        await using var feature = new DataBroadcastSettingsFeature();
        await feature.PutAsync("""{"postalCode":"0000000","prefecture":0}""");
        feature.Clock.Now = DataBroadcastSettingsFeature.Noon.AddMinutes(5);

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(json);
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("postalCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("prefecture").ValueKind);
        Assert.Equal(DataBroadcastSettingsFeature.Noon.AddMinutes(5), data.GetProperty("changedAt").GetDateTime());
    }

    [Fact(DisplayName = "BR-BD-016: sending what stands writes nothing and keeps when it last changed")]
    public async Task SendingWhatStandsWritesNothing()
    {
        await using var feature = new DataBroadcastSettingsFeature();
        await feature.PutAsync("""{"postalCode":"0000000","prefecture":49}""");
        feature.Clock.Now = DataBroadcastSettingsFeature.Noon.AddMinutes(10);

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync("""{"postalCode":"0000000","prefecture":49}""");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(DataBroadcastSettingsFeature.Noon, body.GetProperty("data").GetProperty("changedAt").GetDateTime());
        Assert.Equal((1, 2), (feature.Settings.Saves, feature.Settings.Turns));
    }

    [Theory(DisplayName = "BR-BD-016: a postal code that is not seven digits is refused naming it, and nothing is written")]
    [InlineData("""{"postalCode":"000000","prefecture":null}""")]
    [InlineData("""{"postalCode":"00000000","prefecture":null}""")]
    [InlineData("""{"postalCode":"000-0000","prefecture":null}""")]
    [InlineData("""{"postalCode":"０００００００","prefecture":null}""")]
    [InlineData("""{"postalCode":" ","prefecture":13}""")]
    public async Task APostalCodeThatIsNotSevenDigitsIsRefused(string json)
    {
        await using var feature = new DataBroadcastSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.StartsWith("postalCode", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.Equal((0, 0), (feature.Settings.Saves, feature.Settings.Turns));
    }

    [Theory(DisplayName = "BR-BD-016: a prefecture outside 0 to 49 is refused naming it, and nothing is written")]
    [InlineData(-1)]
    [InlineData(50)]
    [InlineData(255)]
    public async Task APrefectureOutsideTheDesignationIsRefused(int prefecture)
    {
        await using var feature = new DataBroadcastSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync($$"""{"postalCode":"0000000","prefecture":{{prefecture}}}""");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.StartsWith("prefecture", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal((0, 0), (feature.Settings.Saves, feature.Settings.Turns));
    }

    [Theory(DisplayName = "BR-BD-016: a change leaving out either value, or naming anything else, is refused, and nothing is written")]
    [InlineData("{}", "the body")]
    [InlineData("""{"postalCode":"0000000"}""", "the body")]
    [InlineData("""{"prefecture":13}""", "the body")]
    [InlineData("null", "the body")]
    [InlineData("""{"postalCode":"0000000","prefecture":13,"regionCode":0}""", "regionCode")]
    [InlineData("""{"postalCode":0,"prefecture":13}""", "postalCode")]
    [InlineData("""{"postalCode":"0000000","prefecture":true}""", "prefecture")]
    public async Task AChangeLeavingOutAValueOrNamingAnythingElseIsRefused(string json, string named)
    {
        await using var feature = new DataBroadcastSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(named, body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(0, feature.Settings.Saves);
    }

    [Fact(DisplayName = "BR-BA-001: the settings are neither read nor changed without a session")]
    public async Task TheSettingsAreNeitherReadNorChangedWithoutASession()
    {
        await using var feature = new DataBroadcastSettingsFeature();
        using HttpClient anonymous = feature.Built.WithTestScheme().CreateClient();
        using var content = new StringContent("""{"postalCode":"0000000","prefecture":13}""", Encoding.UTF8, "application/json");

        using HttpResponseMessage read = await anonymous.GetAsync(new Uri(DataBroadcastSettingsFeature.Path, UriKind.Relative));
        using HttpResponseMessage changed = await anonymous.PutAsync(new Uri(DataBroadcastSettingsFeature.Path, UriKind.Relative), content);

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (read.StatusCode, changed.StatusCode));
        Assert.Equal(0, feature.Settings.Saves);
    }
}
