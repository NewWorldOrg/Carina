using System.Net;
using System.Text;
using System.Text.Json;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Api.Tests.FeatureTest;

public sealed class NotificationGuardTests
{
    private const string OneDestination = "/api/notifications/destinations/00000000-0000-0000-0000-000000000001";

    private const string OneRule = "/api/notifications/rules/00000000-0000-0000-0000-000000000002";

    private const string OneWatch = "/api/notifications/watches/00000000-0000-0000-0000-000000000003";

    private static readonly string[] LettersOfAnAddress =
    [
        NotificationFeature.Secret,
        "/services/T0000",
        "/api/webhooks/1234",
    ];

    public static TheoryData<string, string> Writes => new()
    {
        { "POST", NotificationFeature.Destinations },
        { "PATCH", OneDestination },
        { "DELETE", OneDestination },
        { "POST", $"{OneDestination}/test" },
        { "POST", NotificationFeature.Rules },
        { "PATCH", OneRule },
        { "DELETE", OneRule },
        { "POST", NotificationFeature.Watches },
        { "DELETE", OneWatch },
        { "PUT", NotificationFeature.Settings },
        { "POST", NotificationFeature.PushSubscriptions },
        { "PUT", $"{NotificationFeature.PushSubscriptions}/00000000-0000-0000-0000-000000000001" },
    };

    public static TheoryData<string, string> Reads => new()
    {
        { "GET", NotificationFeature.Destinations },
        { "GET", NotificationFeature.Rules },
        { "GET", NotificationFeature.Watches },
        { "GET", $"{NotificationFeature.Deliveries}?destinationId=00000000-0000-0000-0000-000000000001" },
        { "GET", NotificationFeature.Settings },
        { "GET", NotificationFeature.PushKey },
    };

    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [MemberData(nameof(Writes))]
    [MemberData(nameof(Reads))]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
    {
        await using NotificationFeature feature = new();
        using HttpClient client = feature.Anonymous();

        using HttpResponseMessage response = await SendingAsync(client, method, path, Json());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory(DisplayName = "BR-NA-002: A write that names no origin is refused")]
    [MemberData(nameof(Writes))]
    public async Task AWriteNamingNoOriginIsRefused(string method, string path)
    {
        await using NotificationFeature feature = new();
        using HttpClient client = feature.NamingNoOrigin();

        using HttpResponseMessage response = await SendingAsync(client, method, path, Json());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await SucceededAsync(response));
    }

    [Theory(DisplayName = "BR-NA-002: A write that names another origin is refused")]
    [MemberData(nameof(Writes))]
    public async Task AWriteNamingAnotherOriginIsRefused(string method, string path)
    {
        await using NotificationFeature feature = new();
        using HttpClient client = feature.FromElsewhere();

        using HttpResponseMessage response = await SendingAsync(client, method, path, Json());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await SucceededAsync(response));
    }

    [Theory(DisplayName = "BR-NA-002: A write that carries a form and not JSON is refused")]
    [MemberData(nameof(Writes))]
    public async Task AWriteCarryingAFormIsRefused(string method, string path)
    {
        await using NotificationFeature feature = new();
        using StringContent form = new("name=anything", Encoding.UTF8, "application/x-www-form-urlencoded");

        using HttpResponseMessage response = await SendingAsync(feature.Client, method, path, form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory(DisplayName = "BR-NA-002: A read from another origin is left alone")]
    [MemberData(nameof(Reads))]
    public async Task AReadFromAnotherOriginIsLeftAlone(string method, string path)
    {
        await using NotificationFeature feature = new();
        using HttpClient client = feature.FromElsewhere();

        using HttpResponseMessage response = await SendingAsync(client, method, path, null);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact(DisplayName = "BR-NA-002: A refused write registers nothing, removes nothing and sends nothing")]
    public async Task ARefusedWriteChangesAndSendsNothing()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();
        using HttpClient elsewhere = feature.FromElsewhere();
        using HttpClient unsigned = feature.Anonymous();

        (HttpStatusCode registered, _) = await feature.PostAsync(
            NotificationFeature.Destinations,
            new { name = "書斎", url = NotificationFeature.DiscordUrl },
            elsewhere);
        (HttpStatusCode tried, _) = await feature.PostAsync(NotificationFeature.TrialOf(id), client: elsewhere);
        (HttpStatusCode removed, _) = await feature.DeleteAsync(NotificationFeature.OneOf(id), elsewhere);
        (HttpStatusCode triedUnsigned, _) = await feature.PostAsync(NotificationFeature.TrialOf(id), client: unsigned);

        Assert.Equal(HttpStatusCode.Forbidden, registered);
        Assert.Equal(HttpStatusCode.Forbidden, tried);
        Assert.Equal(HttpStatusCode.Forbidden, removed);
        Assert.Equal(HttpStatusCode.Unauthorized, triedUnsigned);
        NotificationDestination held = Assert.Single(feature.HeldDestinations.Destinations);
        Assert.Equal(id, held.Id.Value);
        Assert.Empty(feature.End.Heard);
    }

    [Fact(DisplayName = "BR-NA-003: No line of the log carries the address even at the finest level, and no answer does")]
    public async Task NeitherTheLogNorAnAnswerCarriesTheAddress()
    {
        RecordedStartup heard = new();
        await using NotificationFeature feature = new(publicOrigin: "https://carina.example", heard: heard);
        List<string> answered = [];

        Guid id = await feature.RegisteredAsync();
        answered.Add(Said(await feature.PostAsync(
            NotificationFeature.Destinations,
            new { name = "書斎", url = "https://hooks.slack.com.elsewhere.example/services/T0000/B0000/made-up-token-wxyz" })));
        answered.Add(Said(await feature.PatchAsync(NotificationFeature.OneOf(id), new { url = NotificationFeature.DiscordUrl })));
        answered.Add(Said(await feature.PatchAsync(NotificationFeature.OneOf(id), new { url = "http://discord.com/api/webhooks/1234/made-up-token-abcd" })));
        answered.Add(Said(await feature.GetAsync(NotificationFeature.Destinations)));
        answered.Add(Said(await feature.PostAsync(NotificationFeature.TrialOf(id))));

        feature.Clock.Now = feature.Clock.Now.AddSeconds(61);
        feature.End.Status = 302;
        feature.End.AnswerHeaders = "Location: https://discord.com/api/webhooks/1234/made-up-token-abcd\r\n";
        answered.Add(Said(await feature.PostAsync(NotificationFeature.TrialOf(id))));

        feature.Clock.Now = feature.Clock.Now.AddSeconds(61);
        feature.End.Status = 500;
        feature.End.AnswerHeaders = string.Empty;
        feature.End.AnswerBody = "{\"message\":\"https://discord.com/api/webhooks/1234/made-up-token-abcd\"}";
        feature.Waiting(id);
        NotificationDeliveries failed = await feature.PassAsync();

        feature.Clock.Now = feature.Clock.Now.AddMinutes(1);
        feature.End.Status = 204;
        feature.End.AnswerBody = string.Empty;
        NotificationDeliveries sent = await feature.PassAsync();

        feature.End.Status = 404;
        feature.Waiting(id);
        NotificationDeliveries refused = await feature.PassAsync();

        answered.Add(Said(await feature.GetAsync($"{NotificationFeature.Deliveries}?destinationId={id}")));
        answered.Add(Said(await feature.GetAsync(NotificationFeature.Destinations)));
        answered.Add(Said(await feature.DeleteAsync(NotificationFeature.OneOf(id))));

        Assert.Equal(1, failed.Failed);
        Assert.Equal(1, sent.Sent);
        Assert.Equal(1, refused.GivenUp);
        Assert.Equal(5, feature.End.Heard.Count);
        Assert.All(feature.End.Heard, call => Assert.Equal("/api/webhooks/1234/made-up-token-abcd", call.Target));
        Assert.Contains(heard.Everything, line => line.Level is LogLevel.Trace or LogLevel.Debug);
        Assert.Contains(heard.Everything, line => line.Category.StartsWith("Carina.Infrastructure.Notifications", StringComparison.Ordinal));
        Assert.All(LettersOfAnAddress, letters =>
        {
            Assert.DoesNotContain(heard.Everything, line => Carries(line, letters));
            Assert.DoesNotContain(answered, answer => answer.Contains(letters, StringComparison.Ordinal));
        });
    }

    [Theory(DisplayName = "BR-NA-003: No line of the log, with what was thrown, carries the address when a send times out, cannot connect or fails on TLS, and no answer does")]
    [InlineData("timedOut")]
    [InlineData("unreachable")]
    [InlineData("tlsFailed")]
    public async Task NeitherTheLogNorAnAnswerCarriesTheAddressWhenTheLineFails(string failure)
    {
        RecordedStartup heard = new();
        await using NotificationFeature feature = new(heard: heard, deadline: TimeSpan.FromMilliseconds(200));
        Guid id = await feature.RegisteredAsync();
        feature.Network.Stalls = failure == "timedOut";
        feature.Network.Refuses = failure == "unreachable";
        feature.Network.Distrusts = failure == "tlsFailed";

        (HttpStatusCode status, JsonElement tried) = await feature.PostAsync(NotificationFeature.TrialOf(id));
        feature.Waiting(id);
        NotificationDeliveries passed = await feature.PassAsync();
        (_, JsonElement listed) = await feature.GetAsync(NotificationFeature.Destinations);
        (_, JsonElement delivered) = await feature.GetAsync($"{NotificationFeature.Deliveries}?destinationId={id}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(failure, tried.GetProperty("data").GetProperty("failure").GetString());
        Assert.Equal(new NotificationDeliveries(0, 1, 0), passed);
        Assert.Equal(failure, listed.GetProperty("data").GetProperty("items")[0].GetProperty("lastFailure").GetString());
        Assert.Empty(feature.End.Heard);
        Assert.Contains(heard.Everything, line => line.Category.StartsWith("Carina.Infrastructure.Notifications", StringComparison.Ordinal));
        Assert.All(LettersOfAnAddress, letters =>
        {
            Assert.DoesNotContain(heard.Everything, line => Carries(line, letters));
            Assert.DoesNotContain(letters, tried.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(letters, listed.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(letters, delivered.GetRawText(), StringComparison.Ordinal);
        });
    }

    [Fact(DisplayName = "BR-NA-003: No line of the log carries a browser's endpoint or its keys even at the finest level, and no answer does")]
    public async Task NeitherTheLogNorAnAnswerCarriesWhatABrowserSubscribed()
    {
        RecordedStartup heard = new();
        await using NotificationFeature feature = new(publicOrigin: NotificationFeature.Screens, heard: heard);
        using PushReceiver browser = new();
        using PushReceiver renewed = new();
        string[] letters =
        [
            "made-up-registration",
            browser.KeyAsWritten,
            browser.SecretAsWritten,
            renewed.KeyAsWritten,
            renewed.SecretAsWritten,
        ];
        List<string> answered = [];

        Guid id = await feature.SubscribedAsync(browser);
        answered.Add(Said(await feature.PostAsync(
            NotificationFeature.PushSubscriptions,
            new
            {
                name = "書斎のブラウザ",
                endpoint = "https://fcm.googleapis.com.elsewhere.example/made-up-registration-wxyz",
                keys = new { p256dh = browser.KeyAsWritten, auth = browser.SecretAsWritten },
            })));
        answered.Add(Said(await feature.PostAsync(
            NotificationFeature.PushSubscriptions,
            new
            {
                name = "書斎のブラウザ",
                endpoint = NotificationFeature.OtherPushEndpoint,
                keys = new { p256dh = browser.KeyAsWritten, auth = browser.KeyAsWritten },
            })));
        answered.Add(Said(await feature.PutAsync(NotificationFeature.SubscriptionOf(id), NotificationFeature.Subscription(renewed))));
        answered.Add(Said(await feature.PatchAsync(NotificationFeature.OneOf(id), new { url = NotificationFeature.PushEndpoint })));
        answered.Add(Said(await feature.GetAsync(NotificationFeature.Destinations)));
        answered.Add(Said(await feature.GetAsync(NotificationFeature.PushKey)));
        feature.End.Status = 201;
        answered.Add(Said(await feature.PostAsync(NotificationFeature.TrialOf(id))));

        feature.Clock.Now = feature.Clock.Now.AddSeconds(61);
        feature.End.Status = 500;
        feature.End.AnswerBody = "{\"message\":\"https://fcm.googleapis.com/fcm/send/made-up-registration-qrst\"}";
        feature.Waiting(id);
        NotificationDeliveries failed = await feature.PassAsync();

        feature.Clock.Now = feature.Clock.Now.AddMinutes(1);
        feature.End.Status = 201;
        feature.End.AnswerBody = string.Empty;
        NotificationDeliveries sent = await feature.PassAsync();

        feature.End.Status = 410;
        feature.Waiting(id);
        NotificationDeliveries refused = await feature.PassAsync();

        answered.Add(Said(await feature.GetAsync($"{NotificationFeature.Deliveries}?destinationId={id}")));
        answered.Add(Said(await feature.GetAsync(NotificationFeature.Destinations)));
        answered.Add(Said(await feature.DeleteAsync(NotificationFeature.OneOf(id))));

        Assert.Equal(1, failed.Failed);
        Assert.Equal(1, sent.Sent);
        Assert.Equal(1, refused.GivenUp);
        Assert.Equal(4, feature.End.Heard.Count);
        Assert.All(feature.End.Heard, call => Assert.Equal("/fcm/send/made-up-registration-qrst", call.Target));
        Assert.Contains(heard.Everything, line => line.Level is LogLevel.Trace or LogLevel.Debug);
        Assert.Contains(heard.Everything, line => line.Category.StartsWith("Carina.Infrastructure.Notifications", StringComparison.Ordinal));
        Assert.All(letters, written =>
        {
            Assert.DoesNotContain(heard.Everything, line => Carries(line, written));
            Assert.DoesNotContain(answered, answer => answer.Contains(written, StringComparison.Ordinal));
        });
    }

    [Theory(DisplayName = "BR-NA-003: No line of the log, with what was thrown, carries a browser's endpoint or its keys when a send times out, cannot connect or fails on TLS")]
    [InlineData("timedOut")]
    [InlineData("unreachable")]
    [InlineData("tlsFailed")]
    public async Task NeitherTheLogNorAnAnswerCarriesWhatABrowserSubscribedWhenTheLineFails(string failure)
    {
        RecordedStartup heard = new();
        await using NotificationFeature feature = new(publicOrigin: NotificationFeature.Screens, heard: heard, deadline: TimeSpan.FromMilliseconds(200));
        using PushReceiver browser = new();
        Guid id = await feature.SubscribedAsync(browser);
        feature.Network.Stalls = failure == "timedOut";
        feature.Network.Refuses = failure == "unreachable";
        feature.Network.Distrusts = failure == "tlsFailed";

        (HttpStatusCode status, JsonElement tried) = await feature.PostAsync(NotificationFeature.TrialOf(id));
        feature.Waiting(id);
        NotificationDeliveries passed = await feature.PassAsync();
        (_, JsonElement listed) = await feature.GetAsync(NotificationFeature.Destinations);
        (_, JsonElement delivered) = await feature.GetAsync($"{NotificationFeature.Deliveries}?destinationId={id}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(failure, tried.GetProperty("data").GetProperty("failure").GetString());
        Assert.Equal(new NotificationDeliveries(0, 1, 0), passed);
        Assert.Equal(failure, listed.GetProperty("data").GetProperty("items")[0].GetProperty("lastFailure").GetString());
        Assert.Empty(feature.End.Heard);
        Assert.Contains(heard.Everything, line => line.Category.StartsWith("Carina.Infrastructure.Notifications", StringComparison.Ordinal));
        Assert.All((string[])["made-up-registration", browser.KeyAsWritten, browser.SecretAsWritten], written =>
        {
            Assert.DoesNotContain(heard.Everything, line => Carries(line, written));
            Assert.DoesNotContain(written, tried.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(written, listed.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(written, delivered.GetRawText(), StringComparison.Ordinal);
        });
    }

    private static bool Carries(SaidLine line, string letters)
        => line.Text.Contains(letters, StringComparison.Ordinal)
            || line.Thrown.Contains(letters, StringComparison.Ordinal)
            || line.Values.Any(value => (value.Value?.ToString() ?? string.Empty).Contains(letters, StringComparison.Ordinal));

    private static string Said((HttpStatusCode Status, JsonElement Body) answer)
        => answer.Body.ValueKind is JsonValueKind.Undefined ? string.Empty : answer.Body.GetRawText();

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
