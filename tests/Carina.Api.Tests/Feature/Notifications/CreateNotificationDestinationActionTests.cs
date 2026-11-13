using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Api.Tests.Feature.Notifications;

[Trait("Category", "Feature")]
public sealed class CreateNotificationDestinationActionTests
{
    private static readonly string[] LettersOfAnAddress =
    [
        NotificationFeature.Secret,
        "/services/T0000",
        "/api/webhooks/1234",
    ];

    private static readonly string[] UsualEvents =
    [
        "recordingTruncated",
        "recordingFailed",
        "encodeFailed",
        "reservationCompeting",
        "reservationMissed",
        "programmeMoved",
        "qualityIncidentDetected",
        "qualityIncidentResolved",
        "deviceDisconnected",
    ];

    [Theory(DisplayName = "BR-NA-001: A caller who has not signed in is refused by every endpoint of the notifications")]
    [InlineData("POST", NotificationFeature.Destinations)]
    public async Task ACallerWhoHasNotSignedInIsRefused(string method, string path)
        => await NotificationGuard.ACallerWhoHasNotSignedInIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names no origin is refused")]
    [InlineData("POST", NotificationFeature.Destinations)]
    public async Task AWriteNamingNoOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingNoOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that names another origin is refused")]
    [InlineData("POST", NotificationFeature.Destinations)]
    public async Task AWriteNamingAnotherOriginIsRefused(string method, string path)
        => await NotificationGuard.AWriteNamingAnotherOriginIsRefusedAsync(method, path);

    [Theory(DisplayName = "BR-NA-002: A write that carries a form and not JSON is refused")]
    [InlineData("POST", NotificationFeature.Destinations)]
    public async Task AWriteCarryingAFormIsRefused(string method, string path)
        => await NotificationGuard.AWriteCarryingAFormIsRefusedAsync(method, path);

    [Fact(DisplayName = "BR-NA-002: A refused write registers nothing, removes nothing and sends nothing")]
    public async Task ARefusedWriteChangesAndSendsNothing()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();
        using HttpClient elsewhere = feature.FromElsewhere();

        (HttpStatusCode registered, _) = await feature.PostAsync(
            NotificationFeature.Destinations,
            new { name = "書斎", url = NotificationFeature.DiscordUrl },
            elsewhere);

        Assert.Equal(HttpStatusCode.Forbidden, registered);
        NotificationDestination held = Assert.Single(feature.HeldDestinations.Destinations);
        Assert.Equal(id, held.Id.Value);
        Assert.Empty(feature.End.Heard);
    }

    [Theory(DisplayName = "BR-NT-002: A destination is of the kind its address names, and the answer carries the last four letters and not the address")]
    [InlineData(NotificationFeature.SlackUrl, "slack", "wxyz")]
    [InlineData(NotificationFeature.DiscordUrl, "discord", "abcd")]
    public async Task TheKindComesFromTheAddress(string url, string kind, string tail)
    {
        await using NotificationFeature feature = new();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(NotificationFeature.Destinations, new { name = "居間", url });

        Assert.Equal(HttpStatusCode.Created, status);
        JsonElement made = body.GetProperty("data");
        Assert.Equal("居間", made.GetProperty("name").GetString());
        Assert.Equal(kind, made.GetProperty("kind").GetString());
        Assert.Equal(tail, made.GetProperty("tail").GetString());
        Assert.True(made.GetProperty("enabled").GetBoolean());
        Assert.True(made.GetProperty("readable").GetBoolean());
        Assert.DoesNotContain(NotificationFeature.Secret, body.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(NotificationFeature.Noon, made.GetProperty("createdAt").GetDateTime());
    }

    [Theory(DisplayName = "BR-NT-001: An address that is not an https webhook of a host that takes them is refused, and the refusal does not repeat it")]
    [InlineData("http://hooks.slack.com/services/T0000/B0000/made-up-token-wxyz")]
    [InlineData("https://hooks.slack.com:8443/services/T0000/B0000/made-up-token-wxyz")]
    [InlineData("https://someone@hooks.slack.com/services/T0000/B0000/made-up-token-wxyz")]
    [InlineData("https://hooks.slack.com.evil.example/services/T0000/B0000/made-up-token-wxyz")]
    [InlineData("https://discord.com/channels/1234/made-up-token-wxyz")]
    [InlineData("https://[::ffff:10.20.30.40]/services/made-up-token-wxyz")]
    [InlineData("made-up-token-wxyz")]
    [InlineData("")]
    public async Task ABadAddressIsRefusedWithoutBeingRepeated(string url)
    {
        await using NotificationFeature feature = new();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(NotificationFeature.Destinations, new { name = "居間", url });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.DoesNotContain("made-up-token", body.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example", body.GetRawText(), StringComparison.Ordinal);
        Assert.Empty(feature.HeldDestinations.Destinations);
    }

    [Fact(DisplayName = "BR-NT-003: A name is one to sixty-four letters")]
    public async Task ANameIsOneToSixtyFourLetters()
    {
        await using NotificationFeature feature = new();

        (HttpStatusCode blank, _) = await feature.PostAsync(NotificationFeature.Destinations, new { name = "  ", url = NotificationFeature.SlackUrl });
        (HttpStatusCode missing, _) = await feature.PostAsync(NotificationFeature.Destinations, new { url = NotificationFeature.SlackUrl });
        (HttpStatusCode tooLong, _) = await feature.PostAsync(
            NotificationFeature.Destinations,
            new { name = new string('a', 65), url = NotificationFeature.SlackUrl });
        (HttpStatusCode longest, _) = await feature.PostAsync(
            NotificationFeature.Destinations,
            new { name = new string('a', 64), url = NotificationFeature.SlackUrl });

        Assert.Equal(HttpStatusCode.BadRequest, blank);
        Assert.Equal(HttpStatusCode.BadRequest, missing);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong);
        Assert.Equal(HttpStatusCode.Created, longest);
    }

    [Fact(DisplayName = "BR-NT-003: Two destinations may carry the same name and the same address")]
    public async Task TheSameNameAndAddressAreTakenTwice()
    {
        await using NotificationFeature feature = new();

        Guid first = await feature.RegisteredAsync();
        feature.Clock.Now = NotificationFeature.Noon.AddMinutes(1);
        Guid second = await feature.RegisteredAsync();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(NotificationFeature.Destinations);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            [first, second],
            body.GetProperty("data").GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
        Assert.DoesNotContain(NotificationFeature.Secret, body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-NA-003: The address is held sealed, so the row does not carry its letters")]
    public async Task TheAddressIsHeldSealed()
    {
        await using NotificationFeature feature = new();

        await feature.RegisteredAsync();

        NotificationDestination held = Assert.Single(feature.HeldDestinations.Destinations);
        Assert.DoesNotContain(NotificationFeature.Secret, System.Text.Encoding.Latin1.GetString(held.SealedUrl), StringComparison.Ordinal);
        Assert.Equal("wxyz", held.UrlTail);
    }

    [Fact(DisplayName = "D-9: Registering the first destination makes one set of rules that tells it of failures and trouble, and a later destination makes none")]
    public async Task TheFirstDestinationMakesTheUsualRule()
    {
        await using NotificationFeature feature = new();

        Guid first = await feature.RegisteredAsync();
        await feature.RegisteredAsync("書斎", NotificationFeature.DiscordUrl);
        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(NotificationFeature.Rules);

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement usual = Assert.Single(body.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal("失敗と異常", usual.GetProperty("name").GetString());
        Assert.True(usual.GetProperty("enabled").GetBoolean());
        Assert.Equal(UsualEvents, Words(usual.GetProperty("events")));
        Assert.Equal([first], Ids(usual.GetProperty("destinations")));
    }

    [Fact(DisplayName = "D-9: A destination registered while a set of rules is already held makes no other")]
    public async Task ADestinationRegisteredBesideARuleMakesNoOther()
    {
        await using NotificationFeature feature = new();
        NotificationRule held = NotificationRule.Define(
            NotificationRuleId.New(),
            new NotificationName("手で作ったもの"),
            [NoticeKind.RecordingCompleted],
            [NotificationDestinationId.New()],
            NotificationFeature.Noon);
        feature.HeldRules.Rules.Add(held);

        await feature.RegisteredAsync();

        Assert.Same(held, Assert.Single(feature.HeldRules.Rules));
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

    private static bool Carries(SaidLine line, string letters)
        => line.Text.Contains(letters, StringComparison.Ordinal)
            || line.Thrown.Contains(letters, StringComparison.Ordinal)
            || line.Values.Any(value => (value.Value?.ToString() ?? string.Empty).Contains(letters, StringComparison.Ordinal));

    private static string Said((HttpStatusCode Status, JsonElement Body) answer)
        => answer.Body.ValueKind is JsonValueKind.Undefined ? string.Empty : answer.Body.GetRawText();

    private static string[] Words(JsonElement list) => [.. list.EnumerateArray().Select(word => word.GetString() ?? string.Empty)];

    private static Guid[] Ids(JsonElement list) => [.. list.EnumerateArray().Select(id => id.GetGuid())];
}
