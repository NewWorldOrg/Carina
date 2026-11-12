using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Carina.Api.Tests.FeatureTest;

public sealed class NotificationPushEndpointTests
{
    private const string Fingerprint = "fingerprint";

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

    [Fact(DisplayName = "D-11: The key browsers subscribe with is an uncompressed P-256 point in base64url, the same each time, whether or not the public origin is named")]
    public async Task TheKeyBrowsersSubscribeWithIsServed()
    {
        await using NotificationFeature named = new(NotificationFeature.Screens);
        await using NotificationFeature unnamed = new();

        (HttpStatusCode first, JsonElement once) = await named.GetAsync(NotificationFeature.PushKey);
        (HttpStatusCode second, JsonElement again) = await named.GetAsync(NotificationFeature.PushKey);
        (HttpStatusCode third, JsonElement elsewhere) = await unnamed.GetAsync(NotificationFeature.PushKey);

        Assert.Equal(HttpStatusCode.OK, first);
        Assert.Equal(HttpStatusCode.OK, second);
        Assert.Equal(HttpStatusCode.OK, third);
        string written = once.GetProperty("data").GetProperty("publicKey").GetString()!;
        byte[] point = Base64Url.DecodeFromChars(written);
        Assert.Equal(65, point.Length);
        Assert.Equal(0x04, point[0]);
        Assert.DoesNotContain('=', written);
        Assert.Equal(written, again.GetProperty("data").GetProperty("publicKey").GetString());
        Assert.Equal(written, elsewhere.GetProperty("data").GetProperty("publicKey").GetString());
    }

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

    [Fact(DisplayName = "BR-ND-015: A row is given a new subscription by its id, which keeps its name and whether it is on and clears its last result")]
    public async Task ARowIsGivenANewSubscriptionByItsId()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver before = new();
        using PushReceiver after = new();
        Guid id = await feature.SubscribedAsync(before);
        feature.End.Status = 410;
        feature.Waiting(id);
        NotificationDeliveries gone = await feature.PassAsync();
        NotificationDestination held = Assert.Single(feature.HeldDestinations.Destinations);
        bool wasOn = held.Enabled;
        NoticeFailure? failedWith = held.LastFailure;

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            NotificationFeature.SubscriptionOf(id),
            NotificationFeature.Subscription(after, NotificationFeature.OtherPushEndpoint));

        Assert.Equal(1, gone.GivenUp);
        Assert.False(wasOn);
        Assert.Equal(NoticeFailure.Rejected, failedWith);
        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement data = body.GetProperty("data");
        Assert.Equal(id, data.GetProperty("id").GetGuid());
        Assert.Equal("push", data.GetProperty("kind").GetString());
        Assert.Equal("書斎のブラウザ", data.GetProperty("name").GetString());
        Assert.Equal(HashOf(NotificationFeature.OtherPushEndpoint), data.GetProperty(Fingerprint).GetString());
        Assert.False(data.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("lastResultAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("lastFailure").ValueKind);
        AssertNothingOf(after, body.GetRawText());
        Assert.Equal(after.KeyAsWritten, Opened(feature, held).KeyAsWritten);
    }

    [Fact(DisplayName = "BR-ND-015: A new subscription whose endpoint another row holds is refused, and both rows stay as they were")]
    public async Task ANewSubscriptionAnotherRowHoldsIsRefused()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver first = new();
        using PushReceiver second = new();
        Guid one = await feature.SubscribedAsync(first);
        Guid other = await feature.SubscribedAsync(second, "居間のブラウザ", NotificationFeature.OtherPushEndpoint);

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            NotificationFeature.SubscriptionOf(other),
            NotificationFeature.Subscription(second));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal(
            [(one, HashOf(NotificationFeature.PushEndpoint)), (other, HashOf(NotificationFeature.OtherPushEndpoint))],
            feature.HeldDestinations.Destinations.Select(held => (held.Id.Value, held.EndpointHash!)));
        Assert.Equal(first.KeyAsWritten, Opened(feature, feature.HeldDestinations.Destinations[0]).KeyAsWritten);
    }

    [Fact(DisplayName = "BR-ND-015: A row given the endpoint it already holds takes the new keys")]
    public async Task ARowGivenItsOwnEndpointTakesTheNewKeys()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver before = new();
        using PushReceiver after = new();
        Guid id = await feature.SubscribedAsync(before);

        (HttpStatusCode status, _) = await feature.PutAsync(NotificationFeature.SubscriptionOf(id), NotificationFeature.Subscription(after));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(after.KeyAsWritten, Opened(feature, Assert.Single(feature.HeldDestinations.Destinations)).KeyAsWritten);
    }

    [Fact(DisplayName = "BR-ND-015: A new subscription is for a browser's row, so an id nobody holds and the row of a webhook are not found")]
    public async Task ANewSubscriptionIsForABrowsersRow()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid webhook = await feature.RegisteredAsync();
        byte[] before = feature.HeldDestinations.Destinations[0].SealedUrl;

        (HttpStatusCode unknown, _) = await feature.PutAsync(
            NotificationFeature.SubscriptionOf(Guid.NewGuid()),
            NotificationFeature.Subscription(browser));
        (HttpStatusCode ofAWebhook, _) = await feature.PutAsync(
            NotificationFeature.SubscriptionOf(webhook),
            NotificationFeature.Subscription(browser));

        Assert.Equal(HttpStatusCode.NotFound, unknown);
        Assert.Equal(HttpStatusCode.NotFound, ofAWebhook);
        NotificationDestination held = Assert.Single(feature.HeldDestinations.Destinations);
        Assert.Equal(WebhookTarget.Slack, held.Target);
        Assert.Same(before, held.SealedUrl);
        Assert.Null(held.EndpointHash);
    }

    [Fact(DisplayName = "BR-NT-008: A new subscription with a bad endpoint or bad keys is refused and the row keeps what it held")]
    public async Task ABadNewSubscriptionIsRefused()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid id = await feature.SubscribedAsync(browser);

        (HttpStatusCode elsewhere, _) = await feature.PutAsync(
            NotificationFeature.SubscriptionOf(id),
            NotificationFeature.Subscription(browser, "https://elsewhere.example/made-up-registration-qrst"));
        (HttpStatusCode keyless, _) = await feature.PutAsync(
            NotificationFeature.SubscriptionOf(id),
            new { endpoint = NotificationFeature.OtherPushEndpoint, keys = new { p256dh = browser.KeyAsWritten } });

        Assert.Equal(HttpStatusCode.BadRequest, elsewhere);
        Assert.Equal(HttpStatusCode.BadRequest, keyless);
        Assert.Equal(HashOf(NotificationFeature.PushEndpoint), Assert.Single(feature.HeldDestinations.Destinations).EndpointHash);
    }

    [Fact(DisplayName = "BR-NT-008: A change of a browser's row takes its name and whether it is on, and one that names an address is refused and changes nothing")]
    public async Task AChangeOfABrowsersRowTakesNoAddress()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid id = await feature.SubscribedAsync(browser);
        NotificationDestination held = Assert.Single(feature.HeldDestinations.Destinations);
        byte[] before = held.SealedUrl;

        (HttpStatusCode named, JsonElement renamed) = await feature.PatchAsync(NotificationFeature.OneOf(id), new { name = "寝室のブラウザ" });
        (HttpStatusCode turned, JsonElement off) = await feature.PatchAsync(NotificationFeature.OneOf(id), new { enabled = false });
        (HttpStatusCode addressed, JsonElement refusal) = await feature.PatchAsync(
            NotificationFeature.OneOf(id),
            new { name = "玄関のブラウザ", enabled = true, url = NotificationFeature.SlackUrl });
        (HttpStatusCode emptied, _) = await feature.PatchAsync(NotificationFeature.OneOf(id), new { url = string.Empty });

        Assert.Equal(HttpStatusCode.OK, named);
        Assert.Equal("寝室のブラウザ", renamed.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("push", renamed.GetProperty("data").GetProperty("kind").GetString());
        Assert.Equal(HttpStatusCode.OK, turned);
        Assert.False(off.GetProperty("data").GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, addressed);
        Assert.DoesNotContain(NotificationFeature.Secret, refusal.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, emptied);
        Assert.Equal("寝室のブラウザ", held.Name.Value);
        Assert.False(held.Enabled);
        Assert.Equal(WebhookTarget.Push, held.Target);
        Assert.Same(before, held.SealedUrl);
        Assert.Equal(HashOf(NotificationFeature.PushEndpoint), held.EndpointHash);
    }

    [Fact(DisplayName = "BR-NT-008: The list names a browser by its kind and the fingerprint of its endpoint, and a webhook carries no fingerprint")]
    public async Task TheListNamesABrowserByItsFingerprint()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        await feature.RegisteredAsync();
        feature.Clock.Now = NotificationFeature.Noon.AddMinutes(1);
        await feature.SubscribedAsync(browser);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(NotificationFeature.Destinations);

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement items = body.GetProperty("data").GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("slack", items[0].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty(Fingerprint).ValueKind);
        Assert.Equal("push", items[1].GetProperty("kind").GetString());
        Assert.Equal(HashOf(NotificationFeature.PushEndpoint), items[1].GetProperty(Fingerprint).GetString());
        Assert.True(items[1].GetProperty("readable").GetBoolean());
        AssertNothingOf(browser, body.GetRawText());
    }

    [Fact(DisplayName = "D-11: A trial of a browser sends one sample encrypted to it and signed with the key this server serves, and leaves no result on the destination")]
    public async Task ATrialOfABrowserSendsOneEncryptedSample()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid id = await feature.SubscribedAsync(browser);
        feature.End.Status = 201;
        (_, JsonElement served) = await feature.GetAsync(NotificationFeature.PushKey);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(NotificationFeature.TrialOf(id));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("data").GetProperty("delivered").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").GetProperty("failure").ValueKind);
        HeardWebhookCall heard = Assert.Single(feature.End.Heard);
        Assert.Equal("POST", heard.Method);
        Assert.Equal("/fcm/send/made-up-registration-qrst", heard.Target);
        Assert.Equal("fcm.googleapis.com", heard.Said("Host"));
        Assert.Equal("aes128gcm", heard.Said("Content-Encoding"));
        Assert.Equal("application/octet-stream", heard.Said("Content-Type"));
        Assert.Equal("86400", heard.Said("TTL"));
        Assert.Equal("normal", heard.Said("Urgency"));
        VapidReading token = VapidReading.Of(heard.Said("Authorization")!);
        Assert.True(token.Signed);
        Assert.Equal(served.GetProperty("data").GetProperty("publicKey").GetString(), Base64Url.EncodeToString(token.Key));
        Assert.Equal("ES256", token.Header.GetProperty("alg").GetString());
        Assert.Equal("https://fcm.googleapis.com", token.Claims.GetProperty("aud").GetString());
        Assert.Equal(NotificationFeature.Screens, token.Claims.GetProperty("sub").GetString());
        long expires = token.Claims.GetProperty("exp").GetInt64();
        long now = new DateTimeOffset(NotificationFeature.Noon).ToUnixTimeSeconds();
        Assert.InRange(expires, now + 1, now + 86_400);
        using JsonDocument said = JsonDocument.Parse(browser.Open(heard.Sent));
        Assert.Equal("テスト送信", said.RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrEmpty(said.RootElement.GetProperty("body").GetString()));
        Assert.Equal("/settings/notifications", said.RootElement.GetProperty("path").GetString());
        Assert.Equal(JsonValueKind.Null, said.RootElement.GetProperty("id").ValueKind);
        Assert.DoesNotContain("テスト送信", heard.Body, StringComparison.Ordinal);
        Assert.Null(feature.HeldDestinations.Destinations[0].LastResultAt);
    }

    [Theory(DisplayName = "BR-NS-002: A trial the push service does not take answers with the word of the failure and leaves the browser's row on")]
    [InlineData(500, "serverFailed")]
    [InlineData(404, "rejected")]
    [InlineData(410, "rejected")]
    [InlineData(401, "rejected")]
    [InlineData(403, "rejected")]
    [InlineData(413, "rejected")]
    [InlineData(429, "rateLimited")]
    public async Task AFailedTrialOfABrowserNamesTheFailure(int answered, string word)
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid id = await feature.SubscribedAsync(browser);
        feature.End.Status = answered;
        feature.End.AnswerBody = "{\"reason\":\"made-up answer of the other end\"}";

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(NotificationFeature.TrialOf(id));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("data").GetProperty("delivered").GetBoolean());
        Assert.Equal(word, body.GetProperty("data").GetProperty("failure").GetString());
        Assert.DoesNotContain("made-up answer", body.GetRawText(), StringComparison.Ordinal);
        NotificationDestination held = Assert.Single(feature.HeldDestinations.Destinations);
        Assert.True(held.Enabled);
        Assert.Null(held.LastFailure);
    }

    [Theory(DisplayName = "D-8: A push service whose name answers to an address that is not public is not connected to")]
    [InlineData("10.0.0.7")]
    [InlineData("192.168.1.7")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("fd00::7")]
    public async Task APushServiceAtAnAddressThatIsNotPublicIsNotConnectedTo(string address)
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid id = await feature.SubscribedAsync(browser);
        feature.Network.Resolved = IPAddress.Parse(address);

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(NotificationFeature.TrialOf(id));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("data").GetProperty("delivered").GetBoolean());
        Assert.Equal("unreachable", body.GetProperty("data").GetProperty("failure").GetString());
        Assert.Empty(feature.Network.Connected);
        Assert.Empty(feature.End.Heard);
    }

    [Fact(DisplayName = "D-8: A trial of a browser does not follow where the push service points it")]
    public async Task ATrialOfABrowserDoesNotFollowARedirect()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid id = await feature.SubscribedAsync(browser);
        feature.End.Status = 307;
        feature.End.AnswerHeaders = "Location: https://fcm.googleapis.com/fcm/send/elsewhere\r\n";

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(NotificationFeature.TrialOf(id));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("rejected", body.GetProperty("data").GetProperty("failure").GetString());
        Assert.Single(feature.End.Heard);
        Assert.Single(feature.Network.Connected);
    }

    [Fact(DisplayName = "BR-NC-002: A second trial of one browser within sixty seconds is refused, and the next minute takes one again")]
    public async Task ASecondTrialOfABrowserWithinTheMinuteIsRefused()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid id = await feature.SubscribedAsync(browser);
        feature.End.Status = 201;

        (HttpStatusCode first, _) = await feature.PostAsync(NotificationFeature.TrialOf(id));
        feature.Clock.Now = NotificationFeature.Noon.AddSeconds(59);
        (HttpStatusCode second, _) = await feature.PostAsync(NotificationFeature.TrialOf(id));
        feature.Clock.Now = NotificationFeature.Noon.AddSeconds(60);
        (HttpStatusCode third, _) = await feature.PostAsync(NotificationFeature.TrialOf(id));

        Assert.Equal(HttpStatusCode.OK, first);
        Assert.Equal(HttpStatusCode.TooManyRequests, second);
        Assert.Equal(HttpStatusCode.OK, third);
        Assert.Equal(2, feature.End.Heard.Count);
    }

    [Fact(DisplayName = "BR-ND-012: A browser whose subscription cannot be opened says so in the list, and a trial of it is refused without sending")]
    public async Task ABrowserWhoseSubscriptionCannotBeOpenedSaysSo()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        NotificationDestination sealedElsewhere = NotificationDestination.Subscribe(
            NotificationDestinationId.New(),
            new NotificationName("書斎のブラウザ"),
            PushSubscription.Read(NotificationFeature.PushEndpoint, browser.KeyAsWritten, browser.SecretAsWritten)!,
            [1, 2, 3, 4],
            NotificationFeature.Noon);
        feature.HeldDestinations.Destinations.Add(sealedElsewhere);

        (HttpStatusCode listed, JsonElement list) = await feature.GetAsync(NotificationFeature.Destinations);
        (HttpStatusCode tried, _) = await feature.PostAsync(NotificationFeature.TrialOf(sealedElsewhere.Id.Value));

        Assert.Equal(HttpStatusCode.OK, listed);
        Assert.False(list.GetProperty("data").GetProperty("items")[0].GetProperty("readable").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, tried);
        Assert.Empty(feature.End.Heard);
    }

    [Fact(DisplayName = "D-11: Where the key this server signs with cannot be read, it is not served, a trial of a browser is refused without sending, and the file is left as it was")]
    public async Task WhereTheServerKeyCannotBeReadNothingIsSentToABrowser()
    {
        DirectoryInfo kept = Directory.CreateTempSubdirectory("carina-push-key-");
        string file = Path.Combine(kept.FullName, PushServerKey.FileName);
        await File.WriteAllTextAsync(file, "not a key");

        try
        {
            await using NotificationFeature feature = new(
                NotificationFeature.Screens,
                also: services => services.AddSingleton(
                    new PushServerKey(Options.Create(new SealingKeyOptions { Directory = kept.FullName }))));
            using PushReceiver browser = new();
            Guid id = await feature.SubscribedAsync(browser);

            (HttpStatusCode served, JsonElement refusal) = await feature.GetAsync(NotificationFeature.PushKey);
            (HttpStatusCode tried, _) = await feature.PostAsync(NotificationFeature.TrialOf(id));

            Assert.Equal(HttpStatusCode.Conflict, served);
            Assert.False(refusal.GetProperty("status").GetBoolean());
            Assert.Equal(HttpStatusCode.Conflict, tried);
            Assert.Empty(feature.End.Heard);
            Assert.Equal("not a key", await File.ReadAllTextAsync(file));
        }
        finally
        {
            kept.Delete(recursive: true);
        }
    }

    [Fact(DisplayName = "D-11: A notice goes to a browser encrypted, naming the notice and the screen to open, and the delivery is recorded as sent")]
    public async Task ANoticeGoesToABrowserEncrypted()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid id = await feature.SubscribedAsync(browser);
        feature.End.Status = 201;
        NoticeOnItsWay waiting = feature.Waiting(id);

        NotificationDeliveries passed = await feature.PassAsync();
        (HttpStatusCode status, JsonElement record) = await feature.GetAsync($"{NotificationFeature.Deliveries}?destinationId={id}");

        Assert.Equal(1, passed.Sent);
        HeardWebhookCall heard = Assert.Single(feature.End.Heard);
        Assert.Equal("86400", heard.Said("TTL"));
        Assert.Equal("normal", heard.Said("Urgency"));
        Assert.True(VapidReading.Of(heard.Said("Authorization")!).Signed);
        using JsonDocument said = JsonDocument.Parse(browser.Open(heard.Sent));
        Assert.Equal("録画に失敗しました", said.RootElement.GetProperty("title").GetString());
        Assert.Equal("作り物の番組", said.RootElement.GetProperty("body").GetString());
        Assert.Equal("/", said.RootElement.GetProperty("path").GetString());
        Assert.Equal(waiting.Notice.Id.Value, said.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, status);
        AssertNothingOf(browser, record.GetRawText());
        Assert.DoesNotContain("fcm.googleapis.com", record.GetRawText(), StringComparison.Ordinal);
        NotificationDestination held = Assert.Single(feature.HeldDestinations.Destinations);
        Assert.NotNull(held.LastResultAt);
        Assert.Null(held.LastFailure);
    }

    [Theory(DisplayName = "BR-NS-002: A push service that says the subscription is gone turns the browser's row off, and one that fails is tried again later with the row left on")]
    [InlineData(404, false, 0, 1)]
    [InlineData(410, false, 0, 1)]
    [InlineData(500, true, 1, 0)]
    [InlineData(429, true, 1, 0)]
    public async Task APushServiceThatSaysTheSubscriptionIsGoneTurnsTheRowOff(int answered, bool staysOn, int failed, int givenUp)
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid id = await feature.SubscribedAsync(browser);
        feature.End.Status = answered;
        feature.Waiting(id);

        NotificationDeliveries passed = await feature.PassAsync();

        Assert.Equal(failed, passed.Failed);
        Assert.Equal(givenUp, passed.GivenUp);
        Assert.Single(feature.End.Heard);
        Assert.Equal(staysOn, Assert.Single(feature.HeldDestinations.Destinations).Enabled);
    }

    [Fact(DisplayName = "BR-NT-004: Removing a browser takes it out of the sets of rules, and removes a set left with nowhere to send")]
    public async Task RemovingABrowserTakesItOutOfTheSetsOfRules()
    {
        await using NotificationFeature feature = new(NotificationFeature.Screens);
        using PushReceiver browser = new();
        Guid id = await feature.SubscribedAsync(browser);

        (HttpStatusCode status, _) = await feature.DeleteAsync(NotificationFeature.OneOf(id));
        (HttpStatusCode again, _) = await feature.PostAsync(
            NotificationFeature.PushSubscriptions,
            Asked("書斎のブラウザ", NotificationFeature.PushEndpoint, browser.KeyAsWritten, browser.SecretAsWritten));

        Assert.True(status is HttpStatusCode.OK or HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.Created, again);
        Assert.NotEqual(id, Assert.Single(feature.HeldDestinations.Destinations).Id.Value);
    }

    private static object Asked(string name, string endpoint, string? key, string? secret)
        => new { name, endpoint, keys = new { p256dh = key, auth = secret } };

    private static string HashOf(string endpoint) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));

    private static PushSubscription Opened(NotificationFeature feature, NotificationDestination held)
        => feature.Built.Services.GetRequiredService<PushSubscriptionSeal>().Open(held.SealedUrl)!;

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
