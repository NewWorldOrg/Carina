using System.Net;
using System.Text;
using System.Text.Json;

namespace Carina.Api.Tests.Feature.Notifications;

/// <summary>
/// The checks every endpoint of the notifications passes through: a caller who has not signed in, a write from
/// no origin or another one, a write that carries a form, and a read from another origin.
/// </summary>
internal static class NotificationGuard
{
    public const string OneDestination = "/api/notifications/destinations/00000000-0000-0000-0000-000000000001";

    public const string OneRule = "/api/notifications/rules/00000000-0000-0000-0000-000000000002";

    public const string OneWatch = "/api/notifications/watches/00000000-0000-0000-0000-000000000003";

    public static async Task ACallerWhoHasNotSignedInIsRefusedAsync(string method, string path)
    {
        await using NotificationFeature feature = new();
        using HttpClient client = feature.Anonymous();

        using HttpResponseMessage response = await SendingAsync(client, method, path, Json());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public static async Task AWriteNamingNoOriginIsRefusedAsync(string method, string path)
    {
        await using NotificationFeature feature = new();
        using HttpClient client = feature.NamingNoOrigin();

        using HttpResponseMessage response = await SendingAsync(client, method, path, Json());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await SucceededAsync(response));
    }

    public static async Task AWriteNamingAnotherOriginIsRefusedAsync(string method, string path)
    {
        await using NotificationFeature feature = new();
        using HttpClient client = feature.FromElsewhere();

        using HttpResponseMessage response = await SendingAsync(client, method, path, Json());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await SucceededAsync(response));
    }

    public static async Task AWriteCarryingAFormIsRefusedAsync(string method, string path)
    {
        await using NotificationFeature feature = new();
        using StringContent form = new("name=anything", Encoding.UTF8, "application/x-www-form-urlencoded");

        using HttpResponseMessage response = await SendingAsync(feature.Client, method, path, form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    public static async Task AReadFromAnotherOriginIsLeftAloneAsync(string method, string path)
    {
        await using NotificationFeature feature = new();
        using HttpClient client = feature.FromElsewhere();

        using HttpResponseMessage response = await SendingAsync(client, method, path, null);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    private static StringContent Json() => new("{}", Encoding.UTF8, "application/json");

    private static async Task<HttpResponseMessage> SendingAsync(HttpClient client, string method, string path, HttpContent? content)
    {
        using HttpRequestMessage asking = new(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = content,
        };

        return await client.SendAsync(asking);
    }

    private static async Task<bool> SucceededAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("status").GetBoolean();
    }
}
