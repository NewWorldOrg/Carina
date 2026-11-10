using System.Net;
using System.Text;
using System.Text.Json;

using Carina.Domain.Notifications;

namespace Carina.Api.Tests.FeatureTest;

public sealed class NotificationDestinationEndpointTests
{
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
        Assert.DoesNotContain(NotificationFeature.Secret, Encoding.Latin1.GetString(held.SealedUrl), StringComparison.Ordinal);
        Assert.Equal("wxyz", held.UrlTail);
    }

    [Fact(DisplayName = "A change that names only the name keeps the address, and an empty address keeps the held one")]
    public async Task AChangeOfTheNameKeepsTheAddress()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();
        byte[] before = feature.HeldDestinations.Destinations[0].SealedUrl;
        feature.Clock.Now = NotificationFeature.Noon.AddMinutes(5);

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(NotificationFeature.OneOf(id), new { name = "書斎", url = "" });

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement changed = body.GetProperty("data");
        Assert.Equal("書斎", changed.GetProperty("name").GetString());
        Assert.Equal("slack", changed.GetProperty("kind").GetString());
        Assert.Equal("wxyz", changed.GetProperty("tail").GetString());
        Assert.Equal(NotificationFeature.Noon.AddMinutes(5), changed.GetProperty("changedAt").GetDateTime());
        Assert.Same(before, feature.HeldDestinations.Destinations[0].SealedUrl);
    }

    [Fact(DisplayName = "BR-NT-002: A change of the address changes the kind with it")]
    public async Task AChangeOfTheAddressChangesTheKind()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            NotificationFeature.OneOf(id),
            new { url = NotificationFeature.DiscordUrl });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("discord", body.GetProperty("data").GetProperty("kind").GetString());
        Assert.Equal("abcd", body.GetProperty("data").GetProperty("tail").GetString());
        Assert.Equal("居間", body.GetProperty("data").GetProperty("name").GetString());
        Assert.DoesNotContain(NotificationFeature.Secret, body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-NT-001: A change to a bad address or a bad name is refused and nothing is changed")]
    public async Task ABadChangeIsRefused()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();

        (HttpStatusCode badUrl, JsonElement body) = await feature.PatchAsync(
            NotificationFeature.OneOf(id),
            new { name = "書斎", url = "https://hooks.slack.com.evil.example/services/made-up-token-wxyz" });
        (HttpStatusCode badName, _) = await feature.PatchAsync(NotificationFeature.OneOf(id), new { name = new string('a', 65) });
        (HttpStatusCode unknown, _) = await feature.PatchAsync(NotificationFeature.OneOf(Guid.NewGuid()), new { name = "書斎" });

        Assert.Equal(HttpStatusCode.BadRequest, badUrl);
        Assert.DoesNotContain("evil.example", body.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, badName);
        Assert.Equal(HttpStatusCode.NotFound, unknown);
        Assert.Equal("居間", feature.HeldDestinations.Destinations[0].Name.Value);
    }

    [Fact(DisplayName = "BR-NS-002: Turning a destination off gives up what was waiting for it, and turning it on again sends none of that")]
    public async Task TurningOffGivesUpWhatWasWaiting()
    {
        await using NotificationFeature feature = new();
        Guid id = await feature.RegisteredAsync();
        Guid other = await feature.RegisteredAsync("書斎", NotificationFeature.DiscordUrl);
        NoticeOnItsWay waiting = feature.Waiting(id);
        NoticeOnItsWay elsewhere = feature.Waiting(other);

        (HttpStatusCode off, JsonElement offBody) = await feature.PatchAsync(NotificationFeature.OneOf(id), new { enabled = false });
        (HttpStatusCode on, JsonElement onBody) = await feature.PatchAsync(NotificationFeature.OneOf(id), new { enabled = true });

        Assert.Equal(HttpStatusCode.OK, off);
        Assert.False(offBody.GetProperty("data").GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, on);
        Assert.True(onBody.GetProperty("data").GetProperty("enabled").GetBoolean());
        Assert.Equal(NotificationDeliveryState.GivenUp, waiting.Delivery.State);
        Assert.Equal(NoticeFailure.DestinationDisabled, waiting.Delivery.LastFailure);
        Assert.Equal(NotificationDeliveryState.Queued, elsewhere.Delivery.State);
        Assert.Empty(feature.End.Heard);
    }

    [Fact(DisplayName = "Removing a destination takes it out of the sets of rules, and removes a set left with nowhere to send")]
    public async Task RemovingTakesItOutOfTheRules()
    {
        await using NotificationFeature feature = new();
        Guid gone = await feature.RegisteredAsync();
        Guid kept = await feature.RegisteredAsync("書斎", NotificationFeature.DiscordUrl);
        feature.HeldRules.Rules.Clear();
        NotificationRule both = NotificationRule.Define(
            NotificationRuleId.New(),
            new NotificationName("両方へ"),
            [NoticeKind.RecordingFailed],
            [new NotificationDestinationId(gone), new NotificationDestinationId(kept)],
            NotificationFeature.Noon);
        NotificationRule only = NotificationRule.Define(
            NotificationRuleId.New(),
            new NotificationName("片方だけ"),
            [NoticeKind.EncodeFailed],
            [new NotificationDestinationId(gone)],
            NotificationFeature.Noon);
        feature.HeldRules.Rules.AddRange([both, only]);

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync(NotificationFeature.OneOf(gone));
        (HttpStatusCode again, _) = await feature.DeleteAsync(NotificationFeature.OneOf(gone));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(gone, body.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, again);
        Assert.Equal([kept], feature.HeldDestinations.Destinations.Select(destination => destination.Id.Value));
        NotificationRule left = Assert.Single(feature.HeldRules.Rules);
        Assert.Same(both, left);
        Assert.Equal([kept], left.Destinations.Select(destination => destination.Value));
    }

    [Fact(DisplayName = "A trial sends one fixed sample at once and answers whether it was taken")]
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

    [Theory(DisplayName = "A trial the other end does not take answers with the word of the failure and leaves the destination as it was")]
    [InlineData(500, "serverFailed")]
    [InlineData(404, "rejected")]
    [InlineData(401, "rejected")]
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

    [Fact(DisplayName = "BR-ND-012: A destination whose address cannot be opened says so in the list, and a trial of it is refused without sending")]
    public async Task AnUnreadableDestinationSaysSo()
    {
        await using NotificationFeature feature = new();
        NotificationDestination sealedElsewhere = NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName("居間"),
            WebhookTarget.Slack,
            [1, 2, 3, 4],
            "wxyz",
            NotificationFeature.Noon);
        feature.HeldDestinations.Destinations.Add(sealedElsewhere);

        (HttpStatusCode listed, JsonElement list) = await feature.GetAsync(NotificationFeature.Destinations);
        (HttpStatusCode tried, _) = await feature.PostAsync(NotificationFeature.TrialOf(sealedElsewhere.Id.Value));

        Assert.Equal(HttpStatusCode.OK, listed);
        Assert.False(list.GetProperty("data").GetProperty("items")[0].GetProperty("readable").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, tried);
        Assert.Empty(feature.End.Heard);
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
}
