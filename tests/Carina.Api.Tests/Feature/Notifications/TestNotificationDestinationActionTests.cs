using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class TestNotificationDestinationActionTests
{
    private static readonly string[] LettersOfAnAddress =
    [
        NotificationFeature.Secret,
        "/services/T0000",
        "/api/webhooks/1234",
    ];

    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("POST", $"{NotificationGuard.OneDestination}/test")]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names no origin is refused")]
    [InlineData("POST", $"{NotificationGuard.OneDestination}/test")]
    public async Task AWriteNamingNoOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingNoOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names another origin is refused")]
    [InlineData("POST", $"{NotificationGuard.OneDestination}/test")]
    public async Task AWriteNamingAnotherOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingAnotherOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that carries a form and not JSON is refused")]
    [InlineData("POST", $"{NotificationGuard.OneDestination}/test")]
    public async Task AWriteCarryingAFormIsRefused(string method, string path)
        => await NotificationGuard.AWriteCarryingAFormIsRefusedAsync(method, path);

    [Fact(DisplayName = "BR-NA-002: A refused write registers nothing, removes nothing and sends nothing")]
    public async Task ARefusedWriteChangesAndSendsNothing()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();
        using HttpClient elsewhere = feature.FromElsewhere();
        using HttpClient unsigned = feature.Anonymous();

        (HttpStatusCode tried, _) = await feature.PostAsync(NotificationFeature.TrialOf(id), client: elsewhere);
        (HttpStatusCode triedUnsigned, _) = await feature.PostAsync(NotificationFeature.TrialOf(id), client: unsigned);

        Assert.Equal(HttpStatusCode.Forbidden, tried);
        Assert.Equal(HttpStatusCode.Unauthorized, triedUnsigned);
        NotificationDestination held = Assert.Single(feature.HeldDestinations.Destinations);
        Assert.Equal(id, held.Id.Value);
        Assert.Empty(feature.End.Heard);
    }

    [Fact(DisplayName = "BR-NS-002: A trial sends one fixed sample at once and answers whether it was taken, and leaves no result on the destination")]
    public async Task ATrialSendsOneSample()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(NotificationFeature.TrialOf(id));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("data").GetProperty("delivered").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").GetProperty("failure").ValueKind);
        HeardWebhookCall heard = Assert.Single(feature.End.Heard);
        Assert.Equal("POST", heard.Method);
        Assert.Equal("/services/T0000/B0000/made-up-token-wxyz", heard.Target);
        Assert.Contains("テスト送信", heard.Body, StringComparison.Ordinal);
        Assert.Null(feature.HeldDestinations.Destinations[0].LastResultAt);
    }

    [Fact(DisplayName = "BR-NC-002: A second trial of one destination within sixty seconds is refused, and the next minute takes one again")]
    public async Task ASecondTrialWithinTheMinuteIsRefused()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();

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

    [Fact(DisplayName = "BR-NC-002: A sixth trial within sixty seconds is refused whichever destination it names")]
    public async Task ASixthTrialWithinTheMinuteIsRefused()
    {
        await using NotificationFeature feature = new();
        List<Guid> ids = [];

        for (int index = 0; index < 6; index++)
        {
            ids.Add(await feature.RegisteredAsync($"送り先 {index}"));
        }

        List<HttpStatusCode> answers = [];

        foreach (Guid id in ids)
        {
            (HttpStatusCode status, _) = await feature.PostAsync(NotificationFeature.TrialOf(id));
            answers.Add(status);
        }

        Assert.All(answers.Take(5), status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, answers[5]);
        Assert.Equal(5, feature.End.Heard.Count);
    }

    [Fact(DisplayName = "BR-NC-002: Five trials within sixty seconds leave no room for a sixth, and sixty seconds after them a trial is taken again")]
    public async Task TheRoomForTrialsComesBackAfterTheMinute()
    {
        await using NotificationFeature feature = new();
        List<Guid> ids = [];

        for (int index = 0; index < 6; index++)
        {
            ids.Add(await feature.RegisteredAsync($"送り先 {index}"));
        }

        foreach (Guid id in ids.Take(5))
        {
            await feature.PostAsync(NotificationFeature.TrialOf(id));
        }

        feature.Clock.Now = NotificationFeature.Noon.AddSeconds(59);
        (HttpStatusCode early, _) = await feature.PostAsync(NotificationFeature.TrialOf(ids[5]));
        feature.Clock.Now = NotificationFeature.Noon.AddSeconds(60);
        (HttpStatusCode later, JsonElement body) = await feature.PostAsync(NotificationFeature.TrialOf(ids[5]));

        Assert.Equal(HttpStatusCode.TooManyRequests, early);
        Assert.Equal(HttpStatusCode.OK, later);
        Assert.True(body.GetProperty("data").GetProperty("delivered").GetBoolean());
        Assert.Equal(6, feature.End.Heard.Count);
    }

    [Fact(DisplayName = "BR-NC-002: A trial of a destination that was sent thirty notices within sixty seconds is refused without sending, and is taken once the minute has passed")]
    public async Task ATrialIsRefusedWhileTheDestinationHasNoRoom()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();

        for (int index = 0; index < 31; index++)
        {
            feature.Waiting(id);
        }

        NotificationDeliveries passed = await feature.PassAsync();
        (HttpStatusCode full, _) = await feature.PostAsync(NotificationFeature.TrialOf(id));
        int heardWhileFull = feature.End.Heard.Count;
        feature.Clock.Now = NotificationFeature.Noon.AddSeconds(60);
        (HttpStatusCode later, _) = await feature.PostAsync(NotificationFeature.TrialOf(id));

        Assert.Equal(new NotificationDeliveries(30, 0, 0), passed);
        Assert.Equal(HttpStatusCode.TooManyRequests, full);
        Assert.Equal(30, heardWhileFull);
        Assert.Equal(HttpStatusCode.OK, later);
        Assert.Equal(31, feature.End.Heard.Count);
    }

    [Theory(DisplayName = "A trial the other end does not take answers with the word of the failure and leaves the destination as it was")]
    [InlineData(500, "serverFailed")]
    [InlineData(404, "rejected")]
    [InlineData(401, "rejected")]
    [InlineData(403, "rejected")]
    [InlineData(410, "rejected")]
    [InlineData(429, "rateLimited")]
    public async Task AFailedTrialNamesTheFailure(int answered, string word)
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();
        feature.End.Status = answered;
        feature.End.AnswerBody = "{\"message\":\"made-up answer of the other end\"}";

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(NotificationFeature.TrialOf(id));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("data").GetProperty("delivered").GetBoolean());
        Assert.Equal(word, body.GetProperty("data").GetProperty("failure").GetString());
        Assert.DoesNotContain("made-up answer", body.GetRawText(), StringComparison.Ordinal);
        NotificationDestination held = feature.HeldDestinations.Destinations[0];
        Assert.True(held.Enabled);
        Assert.Null(held.LastFailure);
    }

    [Fact(DisplayName = "A trial does not follow where the other end points it")]
    public async Task ATrialDoesNotFollowARedirect()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();
        feature.End.Status = 302;
        feature.End.AnswerHeaders = "Location: https://hooks.slack.com/services/elsewhere\r\n";

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(NotificationFeature.TrialOf(id));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("data").GetProperty("delivered").GetBoolean());
        Assert.Equal("rejected", body.GetProperty("data").GetProperty("failure").GetString());
        Assert.Single(feature.End.Heard);
        Assert.Single(feature.Network.Connected);
    }

    [Fact(DisplayName = "A trial of a destination nobody registered is not found")]
    public async Task ATrialOfAnUnknownDestinationIsNotFound()
    {
        await using NotificationFeature feature = new();

        (HttpStatusCode status, _) = await feature.PostAsync(NotificationFeature.TrialOf(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact(DisplayName = "The link of the sample is built on the public origin, and left out where none is named")]
    public async Task TheSampleCarriesALinkOnlyWithAPublicOrigin()
    {
        await using NotificationFeature named = new("https://screens.example");
        await using NotificationFeature unnamed = new();
        Guid withLink = await named.RegisteredAsync();
        Guid without = await unnamed.RegisteredAsync();

        (HttpStatusCode first, _) = await named.PostAsync(NotificationFeature.TrialOf(withLink));
        (HttpStatusCode second, _) = await unnamed.PostAsync(NotificationFeature.TrialOf(without));

        Assert.Equal(HttpStatusCode.OK, first);
        Assert.Equal(HttpStatusCode.OK, second);
        Assert.Contains("https://screens.example/settings/notifications", Assert.Single(named.End.Heard).Body, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", Assert.Single(unnamed.End.Heard).Body, StringComparison.Ordinal);
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

    private static bool Carries(SaidLine line, string letters)
        => line.Text.Contains(letters, StringComparison.Ordinal)
            || line.Thrown.Contains(letters, StringComparison.Ordinal)
            || line.Values.Any(value => (value.Value?.ToString() ?? string.Empty).Contains(letters, StringComparison.Ordinal));
}
