using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class CreatePushSubscriptionActionTests
{
    public static TheoryData<string> EndpointsNoNoticeGoesTo => new()
    {
        "http://fcm.googleapis.com/fcm/send/made-up-registration-qrst",
        "https://fcm.googleapis.com:8443/fcm/send/made-up-registration-qrst",
        "https://someone@fcm.googleapis.com/fcm/send/made-up-registration-qrst",
        "https://fcm.googleapis.com.elsewhere.example/fcm/send/made-up-registration-qrst",
        "https://elsewhere.example/fcm/send/made-up-registration-qrst",
        "https://notify.windows.com/w/made-up-registration-qrst",
        "https://10.0.0.7/fcm/send/made-up-registration-qrst",
        "https://192.168.1.7/fcm/send/made-up-registration-qrst",
        "https://127.0.0.1/fcm/send/made-up-registration-qrst",
        "https://[::1]/fcm/send/made-up-registration-qrst",
        "https://localhost/fcm/send/made-up-registration-qrst",
        "fcm.googleapis.com/fcm/send/made-up-registration-qrst",
        "",
    };

    private const string Fingerprint = "fingerprint";

    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("POST", NotificationFeature.PushSubscriptions)]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names no origin is refused")]
    [InlineData("POST", NotificationFeature.PushSubscriptions)]
    public async Task AWriteNamingNoOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingNoOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names another origin is refused")]
    [InlineData("POST", NotificationFeature.PushSubscriptions)]
    public async Task AWriteNamingAnotherOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingAnotherOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that carries a form and not JSON is refused")]
    [InlineData("POST", NotificationFeature.PushSubscriptions)]
    public async Task AWriteCarryingAFormIsRefused(string method, string path)
        => await NotificationGuard.AWriteCarryingAFormIsRefusedAsync(method, path);

    [Fact(DisplayName = "BR-NT-008: A browser is registered as a destination of the kind push, and the answer carries the fingerprint of its endpoint and nothing of what it subscribed")]
    public async Task ABrowserIsRegisteredAsADestination()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            NotificationFeature.PushSubscriptions,
            Asked("書斎のブラウザ", NotificationFeature.PushEndpoint, browser.KeyAsWritten, browser.SecretAsWritten));

        Assert.Equal(HttpStatusCode.Created, status);
        JsonElement data = body.GetProperty("data");
        string fingerprint = HashOf(NotificationFeature.PushEndpoint);
        Assert.Equal("push", data.GetProperty("kind").GetString());
        Assert.Equal("書斎のブラウザ", data.GetProperty("name").GetString());
        Assert.Equal(fingerprint, data.GetProperty(Fingerprint).GetString());
        Assert.Equal(fingerprint[^4..], data.GetProperty("tail").GetString());
        Assert.True(data.GetProperty("enabled").GetBoolean());
        Assert.True(data.GetProperty("readable").GetBoolean());
        AssertNothingOf(browser, body.GetRawText());
        NotificationDestination held = Assert.Single(feature.HeldDestinations.Destinations);
        Assert.Equal(WebhookTarget.Push, held.Target);
        Assert.Equal(fingerprint, held.EndpointHash);
        Assert.DoesNotContain("made-up-registration", Encoding.UTF8.GetString(held.SealedUrl), StringComparison.Ordinal);
        Assert.DoesNotContain(browser.SecretAsWritten, Encoding.UTF8.GetString(held.SealedUrl), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-NT-008: A browser of each push service this server knows is registered, and anything else a browser sends beside the subscription is left unread")]
    public async Task ABrowserOfEachPushServiceIsRegistered()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        string[] endpoints =
        [
            NotificationFeature.PushEndpoint,
            NotificationFeature.OtherPushEndpoint,
            "https://web.push.apple.com/made-up-registration-abcd",
            "https://made-up.notify.windows.com/w/?token=made-up-registration-efgh",
        ];
        List<HttpStatusCode> answers = [];

        foreach (string endpoint in endpoints)
        {
            (HttpStatusCode status, _) = await feature.PostAsync(
                NotificationFeature.PushSubscriptions,
                new
                {
                    name = "書斎のブラウザ",
                    endpoint,
                    expirationTime = (long?)null,
                    keys = new { p256dh = browser.KeyAsWritten, auth = browser.SecretAsWritten },
                });
            answers.Add(status);
        }

        Assert.All(answers, status => Assert.Equal(HttpStatusCode.Created, status));
        Assert.Equal(endpoints.Select(HashOf), feature.HeldDestinations.Destinations.Select(held => held.EndpointHash));
    }

    [Theory(DisplayName = "BR-NT-008: An endpoint that is not an https address of a push service this server knows is refused, and the refusal does not repeat it")]
    [MemberData(nameof(EndpointsNoNoticeGoesTo))]
    public async Task AnEndpointNoNoticeGoesToIsRefused(string endpoint)
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            NotificationFeature.PushSubscriptions,
            Asked("書斎のブラウザ", endpoint, browser.KeyAsWritten, browser.SecretAsWritten));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.DoesNotContain("made-up-registration", body.GetRawText(), StringComparison.Ordinal);
        Assert.Empty(feature.HeldDestinations.Destinations);
        Assert.Empty(feature.HeldRules.Rules);
    }

    [Theory(DisplayName = "BR-NT-008: A key that is not a 65-byte uncompressed point of P-256 in base64url is refused, and so is a secret that is not 16 bytes")]
    [InlineData("shortKey")]
    [InlineData("longKey")]
    [InlineData("compressedKey")]
    [InlineData("offTheCurve")]
    [InlineData("paddedKey")]
    [InlineData("noKey")]
    [InlineData("shortSecret")]
    [InlineData("longSecret")]
    [InlineData("plainBase64Secret")]
    [InlineData("noSecret")]
    public async Task KeysOfTheWrongShapeAreRefused(string wrong)
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        byte[] offTheCurve = [.. browser.Key];
        offTheCurve[^1] ^= 0x01;
        (string? key, string? secret) = wrong switch
        {
            "shortKey" => (Base64Url.EncodeToString(browser.Key.AsSpan(0, 64)), browser.SecretAsWritten),
            "longKey" => (Base64Url.EncodeToString([.. browser.Key, 0x00]), browser.SecretAsWritten),
            "compressedKey" => (Base64Url.EncodeToString([0x02, .. browser.Key.AsSpan(1, 32)]), browser.SecretAsWritten),
            "offTheCurve" => (Base64Url.EncodeToString(offTheCurve), browser.SecretAsWritten),
            "paddedKey" => ($"{browser.KeyAsWritten}=", browser.SecretAsWritten),
            "noKey" => (null, browser.SecretAsWritten),
            "shortSecret" => (browser.KeyAsWritten, Base64Url.EncodeToString(browser.Secret.AsSpan(0, 15))),
            "longSecret" => (browser.KeyAsWritten, Base64Url.EncodeToString([.. browser.Secret, 0x00])),
            "plainBase64Secret" => (browser.KeyAsWritten, Convert.ToBase64String(Enumerable.Repeat((byte)0xFB, 16).ToArray())),
            _ => (browser.KeyAsWritten, null),
        };

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            NotificationFeature.PushSubscriptions,
            Asked("書斎のブラウザ", NotificationFeature.PushEndpoint, key, secret));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.DoesNotContain(browser.KeyAsWritten, body.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(browser.SecretAsWritten, body.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("made-up-registration", body.GetRawText(), StringComparison.Ordinal);
        Assert.Empty(feature.HeldDestinations.Destinations);
    }

    [Fact(DisplayName = "BR-NT-008: A registration that carries no keys at all is refused")]
    public async Task ARegistrationWithoutKeysIsRefused()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);

        (HttpStatusCode status, _) = await feature.PostAsync(
            NotificationFeature.PushSubscriptions,
            new { name = "書斎のブラウザ", endpoint = NotificationFeature.PushEndpoint });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty(feature.HeldDestinations.Destinations);
    }

    [Theory(DisplayName = "BR-NT-003: A browser is named in one to sixty-four letters")]
    [InlineData(0, HttpStatusCode.BadRequest)]
    [InlineData(1, HttpStatusCode.Created)]
    [InlineData(64, HttpStatusCode.Created)]
    [InlineData(65, HttpStatusCode.BadRequest)]
    public async Task ABrowserIsNamedInOneToSixtyFourLetters(int letters, HttpStatusCode expected)
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();

        (HttpStatusCode status, _) = await feature.PostAsync(
            NotificationFeature.PushSubscriptions,
            Asked(new string('居', letters), NotificationFeature.PushEndpoint, browser.KeyAsWritten, browser.SecretAsWritten));

        Assert.Equal(expected, status);
    }

    [Fact(DisplayName = "BR-NT-008: While the public origin is not named, a browser is neither registered nor given a new subscription")]
    public async Task WithoutAPublicOriginNoBrowserIsRegistered()
    {
        await using NotificationFeature feature = new();
        using PushReceiver browser = new();
        NotificationDestination held = Held(feature, browser, NotificationFeature.PushEndpoint);
        byte[] before = held.SealedUrl;

        (HttpStatusCode registered, JsonElement refusal) = await feature.PostAsync(
            NotificationFeature.PushSubscriptions,
            Asked("書斎のブラウザ", NotificationFeature.OtherPushEndpoint, browser.KeyAsWritten, browser.SecretAsWritten));
        (HttpStatusCode replaced, _) = await feature.PutAsync(
            NotificationFeature.SubscriptionOf(held.Id.Value),
            NotificationFeature.Subscription(browser, NotificationFeature.OtherPushEndpoint));
        (HttpStatusCode malformed, _) = await feature.PostAsync(
            NotificationFeature.PushSubscriptions,
            Asked("書斎のブラウザ", "https://elsewhere.example/made-up-registration-qrst", browser.KeyAsWritten, browser.SecretAsWritten));

        Assert.Equal(HttpStatusCode.Conflict, registered);
        Assert.False(refusal.GetProperty("status").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, replaced);
        Assert.Equal(HttpStatusCode.BadRequest, malformed);
        Assert.Same(held, Assert.Single(feature.HeldDestinations.Destinations));
        Assert.Same(before, held.SealedUrl);
        Assert.Equal(HashOf(NotificationFeature.PushEndpoint), held.EndpointHash);
    }

    [Fact(DisplayName = "BR-ND-015: The same endpoint registered again rewrites the row it is held in, and keeps its name, whether it is on and the sets of rules that name it")]
    public async Task TheSameEndpointRegisteredAgainRewritesItsRow()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver before = new();
        using PushReceiver after = new();
        Guid id = await feature.SubscribedAsync(before);
        await feature.PatchAsync(NotificationFeature.OneOf(id), new { enabled = false });
        NotificationRule usual = Assert.Single(feature.HeldRules.Rules);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            NotificationFeature.PushSubscriptions,
            Asked("別の名前", NotificationFeature.PushEndpoint, after.KeyAsWritten, after.SecretAsWritten));

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement data = body.GetProperty("data");
        Assert.Equal(id, data.GetProperty("id").GetGuid());
        Assert.Equal("書斎のブラウザ", data.GetProperty("name").GetString());
        Assert.False(data.GetProperty("enabled").GetBoolean());
        NotificationDestination held = Assert.Single(feature.HeldDestinations.Destinations);
        Assert.Equal(after.KeyAsWritten, Opened(feature, held).KeyAsWritten);
        Assert.Equal(after.SecretAsWritten, Opened(feature, held).SecretAsWritten);
        Assert.Same(usual, Assert.Single(feature.HeldRules.Rules));
        Assert.Equal([id], usual.Destinations.Select(destination => destination.Value));
    }

    [Fact(DisplayName = "D-9: The first browser registered where there are no destinations and no sets of rules makes the usual set, and a later one makes none")]
    public async Task TheFirstBrowserMakesTheUsualSetOfRules()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver first = new();
        using PushReceiver second = new();

        Guid one = await feature.SubscribedAsync(first);
        await feature.SubscribedAsync(second, "居間のブラウザ", NotificationFeature.OtherPushEndpoint);

        NotificationRule usual = Assert.Single(feature.HeldRules.Rules);
        Assert.Equal([one], usual.Destinations.Select(destination => destination.Value));
        Assert.Equal(2, feature.HeldDestinations.Destinations.Count);
    }

    [Fact(DisplayName = "D-9: A browser registered after a webhook makes no set of rules")]
    public async Task ABrowserAfterAWebhookMakesNoSetOfRules()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid webhook = await feature.RegisteredAsync();

        await feature.SubscribedAsync(browser);

        Assert.Equal([webhook], Assert.Single(feature.HeldRules.Rules).Destinations.Select(destination => destination.Value));
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

    private static bool Carries(SaidLine line, string letters)
        => line.Text.Contains(letters, StringComparison.Ordinal)
            || line.Thrown.Contains(letters, StringComparison.Ordinal)
            || line.Values.Any(value => (value.Value?.ToString() ?? string.Empty).Contains(letters, StringComparison.Ordinal));

    private static string Said((HttpStatusCode Status, JsonElement Body) answer)
        => answer.Body.ValueKind is JsonValueKind.Undefined ? string.Empty : answer.Body.GetRawText();

    private static object Asked(string name, string endpoint, string? key, string? secret)
        => new { name, endpoint, keys = new { p256dh = key, auth = secret } };

    private static PushSubscription Opened(NotificationFeature feature, NotificationDestination held)
        => feature.Built.Services.GetRequiredService<PushSubscriptionSeal>().Open(held.SealedUrl)!;

    private static string HashOf(string endpoint) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));

    private static NotificationDestination Held(NotificationFeature feature, PushReceiver browser, string endpoint)
    {
        PushSubscription subscription = PushSubscription.Read(endpoint, browser.KeyAsWritten, browser.SecretAsWritten)!;
        NotificationDestination held = NotificationDestination.Subscribe(
            NotificationDestinationId.New(),
            new NotificationName("書斎のブラウザ"),
            subscription,
            feature.Built.Services.GetRequiredService<PushSubscriptionSeal>().Seal(subscription),
            NotificationFeature.Noon);
        feature.HeldDestinations.Destinations.Add(held);

        return held;
    }

    private static void AssertNothingOf(PushReceiver browser, string text)
    {
        Assert.DoesNotContain("made-up-registration", text, StringComparison.Ordinal);
        Assert.DoesNotContain("googleapis", text, StringComparison.Ordinal);
        Assert.DoesNotContain("mozilla", text, StringComparison.Ordinal);
        Assert.DoesNotContain(browser.KeyAsWritten, text, StringComparison.Ordinal);
        Assert.DoesNotContain(browser.SecretAsWritten, text, StringComparison.Ordinal);
        Assert.DoesNotContain("p256dh", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("endpoint", text, StringComparison.OrdinalIgnoreCase);
    }
}
