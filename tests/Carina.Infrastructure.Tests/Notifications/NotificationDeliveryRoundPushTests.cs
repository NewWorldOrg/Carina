using System.Buffers.Text;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Tests.DataBroadcast;
using Carina.TestSupport;

using Microsoft.AspNetCore.DataProtection;

namespace Carina.Infrastructure.Tests.Notifications;

public sealed class NotificationDeliveryRoundPushTests : IDisposable
{
    private const string PushPath = "/fcm/send/made-up-registration";

    private readonly PushReceiver receiver = new();

    [Fact(DisplayName = "D-11: a notice a push service takes is written as sent, and the destination remembers a success")]
    public async Task ANoticeAPushServiceTakesIsWrittenAsSent()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Of(201, []));
        NotificationDestination destination = bench.Browser(WebhookRig.Browser(receiver));
        NotificationDelivery delivery = bench.Waiting(destination);

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(1, 0, 0), moved);
        Assert.Equal(NotificationDeliveryState.Sent, delivery.State);
        Assert.Equal(bench.Now, delivery.SentAt);
        Assert.Equal(bench.Now, destination.LastResultAt);
        Assert.Null(destination.LastFailure);
        Assert.Equal([destination.Id], bench.Destinations.ResultsKept);
        Assert.Equal([PushPath], bench.Paths);
    }

    [Fact(DisplayName = "BR-ND-007: what a browser opens is the notice as it was worded, with the path of its screen and its id")]
    public async Task WhatABrowserOpensIsTheNoticeAsItWasWorded()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Of(201, []));
        NotificationDestination destination = bench.Browser(WebhookRig.Browser(receiver));
        NotificationDelivery delivery = bench.Waiting(
            destination,
            title: "録画が終わりました",
            link: "https://screens.example/recordings/0f8fad5b-d9cb-469f-a165-70867728950e");

        await bench.PassAsync();

        using JsonDocument read = JsonDocument.Parse(receiver.Open(Assert.Single(bench.Rig.Server.Heard).Body));
        Assert.Equal("録画が終わりました", read.RootElement.GetProperty("title").GetString());
        Assert.Equal("作り物の番組\n作り物の局", read.RootElement.GetProperty("body").GetString());
        Assert.Equal("/recordings/0f8fad5b-d9cb-469f-a165-70867728950e", read.RootElement.GetProperty("path").GetString());
        Assert.Equal(delivery.NoticeId.Value.ToString("D"), read.RootElement.GetProperty("id").GetString());
    }

    [Fact(DisplayName = "RFC 8292 section 2: the message is signed by the server's key for the push service, in the name of the origin of the screens, for less than a day")]
    public async Task TheMessageIsSignedByTheServersKey()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Of(201, []));
        bench.Waiting(bench.Browser(WebhookRig.Browser(receiver)));

        await bench.PassAsync();

        VapidReading token = VapidReading.Of(Assert.Single(bench.Rig.Server.Heard).Headers["Authorization"]);
        long expiry = token.Claims.GetProperty("exp").GetInt64();
        long now = new DateTimeOffset(DeliveryBench.Began).ToUnixTimeSeconds();
        Assert.True(token.Signed);
        Assert.Equal("ES256", token.Header.GetProperty("alg").GetString());
        Assert.Equal(bench.Push.Key.PublicKey(), Base64Url.EncodeToString(token.Key));
        Assert.Equal("https://fcm.googleapis.com", token.Claims.GetProperty("aud").GetString());
        Assert.Equal("https://screens.example", token.Claims.GetProperty("sub").GetString());
        Assert.InRange(expiry, now + 1, now + 86400);
    }

    [Fact(DisplayName = "D-11: a notice is kept by the push service for 24 hours at normal urgency")]
    public async Task ANoticeIsKeptForADayAtNormalUrgency()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Of(201, []));
        bench.Waiting(bench.Browser(WebhookRig.Browser(receiver)));

        await bench.PassAsync();

        HeardRequest heard = Assert.Single(bench.Rig.Server.Heard);
        Assert.Equal("86400", heard.Headers["TTL"]);
        Assert.Equal("normal", heard.Headers["Urgency"]);
        Assert.Equal("aes128gcm", heard.Headers["Content-Encoding"]);
    }

    [Fact(DisplayName = "D-11: the notice that a programme asked for begins is urgent, and is kept until that programme ends")]
    public async Task TheNoticeThatAProgrammeBeginsIsUrgentAndKeptUntilItEnds()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Of(201, []));
        NotificationDestination destination = bench.Browser(WebhookRig.Browser(receiver));
        ProgrammeWatch watch = ProgrammeWatch.Ask(
            ProgrammeWatchId.New(),
            new ProgrammeId(new NetworkId(32000), new ServiceId(1024), new EventId(4100)),
            new WatchedBroadcast(DeliveryBench.Began.AddMinutes(3), DeliveryBench.Began.AddMinutes(33), "A programme", null),
            3,
            [destination.Id],
            DeliveryBench.Began.AddHours(-1));
        bench.Push.Watches.Watches.Add(watch);
        bench.Waiting(destination, kind: NoticeKind.ProgrammeStarting, source: watch.Id.Value);

        await bench.PassAsync();

        HeardRequest heard = Assert.Single(bench.Rig.Server.Heard);
        Assert.Equal("1980", heard.Headers["TTL"]);
        Assert.Equal("high", heard.Headers["Urgency"]);
    }

    [Theory(DisplayName = "D-11: a 404 or a 410 turns the destination off, and what else waited for it is given up unsent")]
    [InlineData(404)]
    [InlineData(410)]
    public async Task ASubscriptionThatIsGoneTurnsTheDestinationOff(int status)
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(status, "gone"));
        NotificationDestination destination = bench.Browser(WebhookRig.Browser(receiver));
        NotificationDelivery first = bench.Waiting(destination, DeliveryBench.Began);
        NotificationDelivery second = bench.Waiting(destination, DeliveryBench.Began.AddSeconds(1));

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(0, 0, 2), moved);
        Assert.False(destination.Enabled);
        Assert.Equal(NoticeFailure.Rejected, destination.LastFailure);
        Assert.Equal(NoticeFailure.Rejected, first.LastFailure);
        Assert.Equal(NotificationDeliveryState.GivenUp, second.State);
        Assert.Equal(NoticeFailure.DestinationDisabled, second.LastFailure);
        Assert.Single(bench.Paths);
    }

    [Theory(DisplayName = "D-11: a 401, a 403 or any other 4xx gives up that one notice and leaves the destination on")]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(413)]
    public async Task AnotherRejectionGivesUpOnlyThatNotice(int status)
    {
        int answering = status;
        await using DeliveryBench bench = new(_ => CannedAnswer.Of(answering, []));
        NotificationDestination destination = bench.Browser(WebhookRig.Browser(receiver));
        NotificationDelivery turnedDown = bench.Waiting(destination, DeliveryBench.Began);

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(0, 0, 1), moved);
        Assert.Equal(NotificationDeliveryState.GivenUp, turnedDown.State);
        Assert.Equal(NoticeFailure.Rejected, turnedDown.LastFailure);
        Assert.True(destination.Enabled);

        answering = 201;
        NotificationDelivery next = bench.Waiting(destination, DeliveryBench.Began);
        await bench.PassAsync();

        Assert.Equal(NotificationDeliveryState.Sent, next.State);
        Assert.Equal(2, bench.Paths.Count);
    }

    [Fact(DisplayName = "BR-NC-001: after a 503 from a push service the notice waits thirty seconds and is then sent again")]
    public async Task AfterAServerFailureTheNoticeWaitsThirtySeconds()
    {
        int status = 503;
        await using DeliveryBench bench = new(_ => CannedAnswer.Of(status, []));
        NotificationDestination destination = bench.Browser(WebhookRig.Browser(receiver));
        NotificationDelivery delivery = bench.Waiting(destination);

        Assert.Equal(new NotificationDeliveries(0, 1, 0), await bench.PassAsync());
        Assert.Equal(DeliveryBench.Began.AddSeconds(30), delivery.NextAttemptAt);
        Assert.Equal(NoticeFailure.ServerFailed, destination.LastFailure);
        Assert.True(destination.Enabled);

        status = 201;
        bench.Clock.Turn(TimeSpan.FromSeconds(29));
        Assert.Equal(NotificationDeliveries.None, await bench.PassAsync());

        bench.Clock.Turn(TimeSpan.FromSeconds(1));
        Assert.Equal(new NotificationDeliveries(1, 0, 0), await bench.PassAsync());
        Assert.Equal(NotificationDeliveryState.Sent, delivery.State);
    }

    [Fact(DisplayName = "BR-NC-001: a 429 from a push service makes the notice wait as long as it asked")]
    public async Task A429MakesTheNoticeWaitAsLongAsAsked()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Of(429, [], "Retry-After: 45"));
        NotificationDelivery delivery = bench.Waiting(bench.Browser(WebhookRig.Browser(receiver)));

        Assert.Equal(new NotificationDeliveries(0, 1, 0), await bench.PassAsync());

        Assert.Equal(NoticeFailure.RateLimited, delivery.LastFailure);
        Assert.Equal(DeliveryBench.Began.AddSeconds(45), delivery.NextAttemptAt);
    }

    [Fact(DisplayName = "BR-ND-012: a browser whose subscription does not open with the keys held is not sent to, and says so, and stays on")]
    public async Task ASubscriptionThatDoesNotOpenIsNotSentTo()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Of(201, []));
        NotificationDestination destination = bench.Browser(
            WebhookRig.Browser(receiver),
            new PushSubscriptionSeal(new EphemeralDataProtectionProvider()));
        NotificationDelivery first = bench.Waiting(destination, DeliveryBench.Began);
        NotificationDelivery second = bench.Waiting(destination, DeliveryBench.Began.AddSeconds(1));

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(0, 0, 2), moved);
        Assert.Equal(NoticeFailure.Unreadable, first.LastFailure);
        Assert.Equal(NoticeFailure.Unreadable, second.LastFailure);
        Assert.True(destination.Enabled);
        Assert.Equal(NoticeFailure.Unreadable, destination.LastFailure);
        Assert.Empty(bench.Paths);
        Assert.Single(bench.Said.Warnings);
    }

    [Fact(DisplayName = "BR-ND-012: with a server key that cannot be read a browser is not sent to and stays on, and a webhook is sent to all the same")]
    public async Task WithAServerKeyThatCannotBeReadABrowserIsNotSentTo()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        bench.Push.Directory.Create();
        File.WriteAllText(Path.Combine(bench.Push.Directory.FullName, PushServerKey.FileName), "not a key");
        NotificationDestination browser = bench.Browser(WebhookRig.Browser(receiver));
        NotificationDelivery toTheBrowser = bench.Waiting(browser);
        NotificationDelivery toTheWebhook = bench.Waiting(bench.Destination(WebhookRig.Slack));

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(1, 0, 1), moved);
        Assert.Equal(NoticeFailure.Unreadable, toTheBrowser.LastFailure);
        Assert.True(browser.Enabled);
        Assert.Equal(NoticeFailure.Unreadable, browser.LastFailure);
        Assert.Equal(NotificationDeliveryState.Sent, toTheWebhook.State);
        Assert.DoesNotContain(PushPath, bench.Paths);
    }

    [Fact(DisplayName = "BR-NC-002: a browser and a webhook are each sent what waits for them, and one that fails does not hold the other up")]
    public async Task ABrowserAndAWebhookAreEachSentWhatWaitsForThem()
    {
        await using DeliveryBench bench = new(heard => heard.Target == PushPath ? CannedAnswer.Of(503, []) : CannedAnswer.Text(200, "ok"));
        NotificationDelivery toTheBrowser = bench.Waiting(bench.Browser(WebhookRig.Browser(receiver)));
        NotificationDelivery toTheWebhook = bench.Waiting(bench.Destination(WebhookRig.Slack));

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(1, 1, 0), moved);
        Assert.Equal(NotificationDeliveryState.Failed, toTheBrowser.State);
        Assert.Equal(NotificationDeliveryState.Sent, toTheWebhook.State);
    }

    [Theory(DisplayName = "BR-NA-003: whatever a push service answers, no line of the log carries the endpoint, either key or the server's token")]
    [InlineData(201)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(410)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task NoLineOfTheLogCarriesTheSubscription(int status)
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(status, "a body nobody keeps: made-up-secret-body"));
        bench.Waiting(bench.Browser(WebhookRig.Browser(receiver)));
        bench.Waiting(bench.Browser(
            WebhookRig.Browser(receiver, "made-up-other-registration"),
            new PushSubscriptionSeal(new EphemeralDataProtectionProvider())));

        await bench.PassAsync();

        string token = Assert.Single(bench.Rig.Server.Heard).Headers["Authorization"];
        Assert.NotEmpty(bench.Said.Said);
        Assert.DoesNotContain(bench.Said.Said, line => line.Contains("made-up", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(bench.Said.Said, line => line.Contains("googleapis", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(bench.Said.Said, line => line.Contains("/fcm/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(bench.Said.Said, line => line.Contains(receiver.KeyAsWritten, StringComparison.Ordinal));
        Assert.DoesNotContain(bench.Said.Said, line => line.Contains(receiver.SecretAsWritten, StringComparison.Ordinal));
        Assert.DoesNotContain(bench.Said.Said, line => line.Contains("vapid", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(bench.Said.Said, line => line.Contains(token.Split('.')[1], StringComparison.Ordinal));
    }

    public void Dispose() => receiver.Dispose();
}
