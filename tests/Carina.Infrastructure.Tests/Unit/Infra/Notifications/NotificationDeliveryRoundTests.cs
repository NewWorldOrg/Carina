using System.Buffers.Text;
using System.Text;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Tests.DataBroadcast;
using Carina.Infrastructure.Tests.Notifications;
using Carina.TestSupport;

using Microsoft.AspNetCore.DataProtection;

namespace Carina.Infrastructure.Tests.Unit.Infra.Notifications;

[Trait("Category", "Unit")]
public sealed class NotificationDeliveryRoundTests : IDisposable
{
    private const string SlackPath = "/services/T0000/B0000/made-up-token-wxyz";

    private const string DiscordPath = "/api/webhooks/1234/made-up-token-abcd";

    private const string PushPath = "/fcm/send/made-up-registration";

    private readonly PushReceiver receiver = new();

    [Fact(DisplayName = "BR-NS-001: a notice a webhook takes is written as sent, and the destination remembers a success")]
    public async Task ANoticeTakenIsWrittenAsSent()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        NotificationDestination destination = bench.Destination(WebhookRig.Slack);
        NotificationDelivery delivery = bench.Waiting(destination);

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(1, 0, 0), moved);
        Assert.Equal(NotificationDeliveryState.Sent, delivery.State);
        Assert.Equal(bench.Now, delivery.SentAt);
        Assert.Equal(1, bench.Deliveries.Saved);
        Assert.Equal(bench.Now, destination.LastResultAt);
        Assert.Null(destination.LastFailure);
        Assert.Equal([destination.Id], bench.Destinations.ResultsKept);
        Assert.Equal(0, bench.Destinations.SavedWhole);
        Assert.Equal([SlackPath], bench.Paths);
    }

    [Fact(DisplayName = "BR-ND-007: what is posted is the notice as it was worded, in the shape of the destination's kind")]
    public async Task WhatIsPostedIsTheNoticeInTheShapeOfTheKind()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        bench.Waiting(bench.Destination(WebhookRig.Slack), title: "ひとつめ");
        bench.Waiting(bench.Destination(WebhookRig.Discord), title: "ふたつめ");

        await bench.PassAsync();

        string toSlack = Encoding.UTF8.GetString(bench.Rig.Server.Heard.Single(heard => heard.Target == SlackPath).Body);
        string toDiscord = Encoding.UTF8.GetString(bench.Rig.Server.Heard.Single(heard => heard.Target == DiscordPath).Body);
        Assert.Contains("\"attachments\"", toSlack, StringComparison.Ordinal);
        Assert.Contains("ひとつめ", toSlack, StringComparison.Ordinal);
        Assert.Contains("\"embeds\"", toDiscord, StringComparison.Ordinal);
        Assert.Contains("\"allowed_mentions\":{\"parse\":[]}", toDiscord, StringComparison.Ordinal);
        Assert.Contains("ふたつめ", toDiscord, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-NC-001: after a 503 the notice waits thirty seconds and is not sent again before then")]
    public async Task AfterAServerFailureTheNoticeWaitsThirtySeconds()
    {
        int status = 503;
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(status, "busy"));
        NotificationDestination destination = bench.Destination(WebhookRig.Slack);
        NotificationDelivery delivery = bench.Waiting(destination);

        NotificationDeliveries first = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(0, 1, 0), first);
        Assert.Equal(NotificationDeliveryState.Failed, delivery.State);
        Assert.Equal(1, delivery.Attempts);
        Assert.Equal(DeliveryBench.Began.AddSeconds(30), delivery.NextAttemptAt);
        Assert.Equal(NoticeFailure.ServerFailed, destination.LastFailure);
        Assert.True(destination.Enabled);

        status = 200;
        bench.Clock.Turn(TimeSpan.FromSeconds(29));
        Assert.Equal(NotificationDeliveries.None, await bench.PassAsync());
        Assert.Single(bench.Paths);

        bench.Clock.Turn(TimeSpan.FromSeconds(1));
        Assert.Equal(new NotificationDeliveries(1, 0, 0), await bench.PassAsync());
        Assert.Equal(NotificationDeliveryState.Sent, delivery.State);
        Assert.Null(destination.LastFailure);
    }

    [Theory(DisplayName = "BR-NC-001: a send that times out, cannot connect or fails on TLS waits thirty seconds, and the destination stays on")]
    [InlineData(NoticeFailure.TimedOut)]
    [InlineData(NoticeFailure.Unreachable)]
    [InlineData(NoticeFailure.TlsFailed)]
    public async Task AFailureOfTheLineWaitsThirtySeconds(NoticeFailure failure)
    {
        TaskCompletionSource heard = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<CannedAnswer> never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using DeliveryBench bench = new(_ =>
        {
            heard.TrySetResult();

            return never.Task;
        });
        bench.Rig.Network.Refuses = failure == NoticeFailure.Unreachable;
        bench.Rig.Network.Trusted = failure == NoticeFailure.TlsFailed ? null : bench.Rig.Network.Trusted;
        NotificationDestination destination = bench.Destination(WebhookRig.Slack);
        NotificationDelivery delivery = bench.Waiting(destination);

        Task<NotificationDeliveries> passing = bench.PassAsync();
        await Task.WhenAny(heard.Task, passing).WaitAsync(TimeSpan.FromSeconds(30));
        bench.Deadlines.Turn(NoticeLine.UsualDeadline);
        NotificationDeliveries moved = await passing.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(new NotificationDeliveries(0, 1, 0), moved);
        Assert.Equal(NotificationDeliveryState.Failed, delivery.State);
        Assert.Equal(failure, delivery.LastFailure);
        Assert.Equal(failure, destination.LastFailure);
        Assert.True(destination.Enabled);
        Assert.Equal(DeliveryBench.Began.AddSeconds(30), delivery.NextAttemptAt);
        Assert.Equal(failure == NoticeFailure.TimedOut ? 1 : 0, bench.Paths.Count);
    }

    [Fact(DisplayName = "BR-NS-001: the fourth failure gives the notice up, after waits of 30 seconds, 2 minutes and 10 minutes")]
    public async Task TheFourthFailureGivesTheNoticeUp()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(500, "down"));
        NotificationDestination destination = bench.Destination(WebhookRig.Slack);
        NotificationDelivery delivery = bench.Waiting(destination);

        await bench.PassAsync();
        bench.Clock.Turn(TimeSpan.FromSeconds(30));
        await bench.PassAsync();
        Assert.Equal(DeliveryBench.Began.AddSeconds(30).AddMinutes(2), delivery.NextAttemptAt);

        bench.Clock.Turn(TimeSpan.FromMinutes(2));
        await bench.PassAsync();
        Assert.Equal(DeliveryBench.Began.AddSeconds(30).AddMinutes(12), delivery.NextAttemptAt);

        bench.Clock.Turn(TimeSpan.FromMinutes(10));
        NotificationDeliveries last = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(0, 1, 1), last);
        Assert.Equal(NotificationDeliveryState.GivenUp, delivery.State);
        Assert.Equal(NoticeFailure.ServerFailed, delivery.LastFailure);
        Assert.Equal(4, delivery.Attempts);
        Assert.Equal(4, bench.Paths.Count);
        Assert.True(destination.Enabled);

        bench.Clock.Turn(TimeSpan.FromHours(1));
        Assert.Equal(NotificationDeliveries.None, await bench.PassAsync());
        Assert.Equal(4, bench.Paths.Count);
    }

    [Fact(DisplayName = "BR-NC-001: a 429 makes the notice wait as long as the other end asked, and the destination is left alone until then")]
    public async Task A429WaitsAsLongAsAsked()
    {
        int status = 429;
        await using DeliveryBench bench = new(_ => CannedAnswer.Of(status, [], "Retry-After: 45"));
        NotificationDestination destination = bench.Destination(WebhookRig.Discord);
        NotificationDelivery first = bench.Waiting(destination, DeliveryBench.Began.AddSeconds(-1));
        NotificationDelivery second = bench.Waiting(destination, DeliveryBench.Began);

        await bench.PassAsync();

        Assert.Equal(NotificationDeliveryState.Failed, first.State);
        Assert.Equal(NoticeFailure.RateLimited, first.LastFailure);
        Assert.Equal(DeliveryBench.Began.AddSeconds(45), first.NextAttemptAt);
        Assert.Equal(NotificationDeliveryState.Queued, second.State);
        Assert.Single(bench.Paths);

        status = 204;
        bench.Clock.Turn(TimeSpan.FromSeconds(44));
        Assert.Equal(NotificationDeliveries.None, await bench.PassAsync());
        Assert.Single(bench.Paths);

        bench.Clock.Turn(TimeSpan.FromSeconds(1));
        Assert.Equal(new NotificationDeliveries(2, 0, 0), await bench.PassAsync());
    }

    [Fact(DisplayName = "BR-NC-001: a 429 that asks for longer than ten minutes waits ten minutes")]
    public async Task A429NeverWaitsLongerThanTenMinutes()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Of(429, [], "Retry-After: 86400"));
        NotificationDelivery delivery = bench.Waiting(bench.Destination(WebhookRig.Slack));

        await bench.PassAsync();

        Assert.Equal(DeliveryBench.Began.AddMinutes(10), delivery.NextAttemptAt);
    }

    [Theory(DisplayName = "BR-NS-002: a 401, a 403, a 404 or a 410 turns the destination off, Slack or Discord, and what else waited for it is given up unsent")]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(410, false)]
    [InlineData(401, true)]
    [InlineData(403, true)]
    [InlineData(404, true)]
    [InlineData(410, true)]
    public async Task AnAddressThatIsGoneTurnsTheDestinationOff(int status, bool toSlack)
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(status, "unknown webhook"));
        NotificationDestination destination = bench.Destination(toSlack ? WebhookRig.Slack : WebhookRig.Discord);
        NotificationDelivery first = bench.Waiting(destination, DeliveryBench.Began);
        NotificationDelivery second = bench.Waiting(destination, DeliveryBench.Began.AddSeconds(1));

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(0, 0, 2), moved);
        Assert.False(destination.Enabled);
        Assert.Equal(NoticeFailure.Rejected, destination.LastFailure);
        Assert.Equal(NotificationDeliveryState.GivenUp, first.State);
        Assert.Equal(NoticeFailure.Rejected, first.LastFailure);
        Assert.Equal(NotificationDeliveryState.GivenUp, second.State);
        Assert.Equal(NoticeFailure.DestinationDisabled, second.LastFailure);
        Assert.Single(bench.Paths);
    }

    [Theory(DisplayName = "BR-NS-001: any other 4xx, and a redirect, gives up that one notice and leaves the destination on")]
    [InlineData(400)]
    [InlineData(402)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    public async Task AnotherRejectionGivesUpOnlyThatNotice(int status)
    {
        int answering = status;
        await using DeliveryBench bench = new(_ => CannedAnswer.Of(answering, [], "Location: https://hooks.slack.com/services/elsewhere"));
        NotificationDestination destination = bench.Destination(WebhookRig.Slack);
        NotificationDelivery turnedDown = bench.Waiting(destination, DeliveryBench.Began);

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(0, 0, 1), moved);
        Assert.Equal(NotificationDeliveryState.GivenUp, turnedDown.State);
        Assert.Equal(NoticeFailure.Rejected, turnedDown.LastFailure);
        Assert.True(destination.Enabled);
        Assert.Equal(NoticeFailure.Rejected, destination.LastFailure);

        answering = 200;
        NotificationDelivery next = bench.Waiting(destination, DeliveryBench.Began);
        await bench.PassAsync();

        Assert.Equal(NotificationDeliveryState.Sent, next.State);
        Assert.Equal(2, bench.Paths.Count);
    }

    [Fact(DisplayName = "BR-NS-001: a notice of something more than an hour old is given up unsent, and one exactly an hour old is sent")]
    public async Task ANoticeMoreThanAnHourOldIsGivenUpUnsent()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        NotificationDestination destination = bench.Destination(WebhookRig.Slack);
        NotificationDelivery tooOld = bench.Waiting(destination, DeliveryBench.Began.AddHours(-1).AddTicks(-1));
        NotificationDelivery onTheEdge = bench.Waiting(destination, DeliveryBench.Began.AddHours(-1));

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(1, 0, 1), moved);
        Assert.Equal(NotificationDeliveryState.GivenUp, tooOld.State);
        Assert.Equal(NoticeFailure.TooOld, tooOld.LastFailure);
        Assert.Equal(NotificationDeliveryState.Sent, onTheEdge.State);
        Assert.Single(bench.Paths);
    }

    [Fact(DisplayName = "BR-NS-001: a notice that was being sent again is given up once what it tells of is more than an hour old")]
    public async Task ANoticeUnderRetryIsGivenUpOnceItIsTooOld()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(503, "busy"));
        NotificationDelivery delivery = bench.Waiting(bench.Destination(WebhookRig.Slack), DeliveryBench.Began.AddMinutes(-59));

        await bench.PassAsync();
        bench.Clock.Turn(TimeSpan.FromSeconds(30));
        await bench.PassAsync();
        bench.Clock.Turn(TimeSpan.FromMinutes(2));
        await bench.PassAsync();

        Assert.Equal(NotificationDeliveryState.GivenUp, delivery.State);
        Assert.Equal(NoticeFailure.TooOld, delivery.LastFailure);
        Assert.Equal(2, bench.Paths.Count);
    }

    [Fact(DisplayName = "BR-ND-012: a destination whose address does not open is not sent to, and says so, and stays on")]
    public async Task ADestinationThatDoesNotOpenIsNotSentTo()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        NotificationDestination destination = bench.SealedWithKeysNowLost(WebhookRig.Slack);
        NotificationDelivery first = bench.Waiting(destination, DeliveryBench.Began);
        NotificationDelivery second = bench.Waiting(destination, DeliveryBench.Began.AddSeconds(1));

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(0, 0, 2), moved);
        Assert.Equal(NoticeFailure.Unreadable, first.LastFailure);
        Assert.Equal(NoticeFailure.Unreadable, second.LastFailure);
        Assert.Equal(NotificationDeliveryState.GivenUp, second.State);
        Assert.True(destination.Enabled);
        Assert.Equal(NoticeFailure.Unreadable, destination.LastFailure);
        Assert.Equal([destination.Id], bench.Destinations.ResultsKept);
        Assert.Empty(bench.Paths);
        Assert.Single(bench.Said.Warnings);
    }

    [Fact(DisplayName = "BR-NS-002: what waits for a destination that is off is given up unsent")]
    public async Task WhatWaitsForADestinationThatIsOffIsGivenUp()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        NotificationDestination destination = bench.Destination(WebhookRig.Slack);
        NotificationDelivery delivery = bench.Waiting(destination);
        destination.Disable(bench.Now);

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(0, 0, 1), moved);
        Assert.Equal(NotificationDeliveryState.GivenUp, delivery.State);
        Assert.Equal(NoticeFailure.DestinationDisabled, delivery.LastFailure);
        Assert.Empty(bench.Paths);
        Assert.Empty(bench.Destinations.ResultsKept);

        destination.Enable(bench.Now);
        Assert.Equal(NotificationDeliveries.None, await bench.PassAsync());
        Assert.Empty(bench.Paths);
    }

    [Fact(DisplayName = "BR-NC-002: a destination is sent what waits for it in the order it happened")]
    public async Task ADestinationIsSentInTheOrderThingsHappened()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        NotificationDestination destination = bench.Destination(WebhookRig.Slack);
        bench.Waiting(destination, DeliveryBench.Began.AddSeconds(-10), title: "third");
        bench.Waiting(destination, DeliveryBench.Began.AddSeconds(-30), title: "first");
        bench.Waiting(destination, DeliveryBench.Began.AddSeconds(-20), title: "second");

        await bench.PassAsync();

        List<string> bodies = [.. bench.Rig.Server.Heard.Select(heard => Encoding.UTF8.GetString(heard.Body))];
        Assert.Equal(3, bodies.Count);
        Assert.Contains("first", bodies[0], StringComparison.Ordinal);
        Assert.Contains("second", bodies[1], StringComparison.Ordinal);
        Assert.Contains("third", bodies[2], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-NC-002: a notice that waits to be sent again does not hold up the ones behind it")]
    public async Task ANoticeUnderRetryDoesNotHoldUpTheOnesBehind()
    {
        int status = 503;
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(status, "busy"));
        NotificationDestination destination = bench.Destination(WebhookRig.Slack);
        NotificationDelivery retried = bench.Waiting(destination, DeliveryBench.Began);
        await bench.PassAsync();

        status = 200;
        bench.Clock.Turn(TimeSpan.FromSeconds(5));
        NotificationDelivery behind = bench.Waiting(destination, bench.Now);

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(1, 0, 0), moved);
        Assert.Equal(NotificationDeliveryState.Failed, retried.State);
        Assert.Equal(NotificationDeliveryState.Sent, behind.State);
    }

    [Fact(DisplayName = "BR-NC-002: a destination takes thirty notices in sixty seconds, and the rest go once the sixty seconds are over")]
    public async Task ADestinationTakesThirtyNoticesInSixtySeconds()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        NotificationDestination destination = bench.Destination(WebhookRig.Slack);

        for (int index = 0; index < 50; index++)
        {
            bench.Waiting(destination, DeliveryBench.Began.AddSeconds(-50 + index));
        }

        Assert.Equal(new NotificationDeliveries(30, 0, 0), await bench.PassAsync());

        bench.Clock.Turn(TimeSpan.FromSeconds(59));
        Assert.Equal(NotificationDeliveries.None, await bench.PassAsync());
        Assert.Equal(30, bench.Paths.Count);

        bench.Clock.Turn(TimeSpan.FromSeconds(1));
        Assert.Equal(new NotificationDeliveries(20, 0, 0), await bench.PassAsync());
        Assert.Equal(50, bench.Paths.Count);
    }

    [Fact(DisplayName = "BR-NC-002: a destination that fails does not keep another destination's notices from going")]
    public async Task ADestinationThatFailsDoesNotHoldUpAnother()
    {
        await using DeliveryBench bench = new(request => request.Target == SlackPath
            ? CannedAnswer.Text(503, "busy")
            : CannedAnswer.Of(204, []));
        NotificationDestination failing = bench.Destination(WebhookRig.Slack);
        NotificationDestination working = bench.Destination(WebhookRig.Discord);
        NotificationDelivery held = bench.Waiting(failing, DeliveryBench.Began);
        NotificationDelivery alsoHeld = bench.Waiting(failing, DeliveryBench.Began);
        NotificationDelivery gone = bench.Waiting(working, DeliveryBench.Began);

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(1, 1, 0), moved);
        Assert.Equal(NotificationDeliveryState.Failed, held.State);
        Assert.Equal(NotificationDeliveryState.Queued, alsoHeld.State);
        Assert.Equal(NotificationDeliveryState.Sent, gone.State);
        Assert.Equal([SlackPath, DiscordPath], bench.Paths);
    }

    [Fact(DisplayName = "BR-NS-001: a delivery whose destination is no longer held is left as it is")]
    public async Task ADeliveryWhoseDestinationIsNotHeldIsLeftAlone()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        NotificationDestination destination = bench.Destination(WebhookRig.Slack);
        NotificationDelivery delivery = bench.Waiting(destination);
        bench.Destinations.Destinations.Clear();

        Assert.Equal(NotificationDeliveries.None, await bench.PassAsync());
        Assert.Equal(NotificationDeliveryState.Queued, delivery.State);
        Assert.Empty(bench.Paths);
    }

    [Fact(DisplayName = "BR-NS-001: a notice that was taken but could not be written as sent is not sent again, and the next pass writes it")]
    public async Task ANoticeTakenButNotWrittenIsNotSentAgain()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        NotificationDestination unwritten = bench.Destination(WebhookRig.Slack);
        NotificationDestination other = bench.Destination(WebhookRig.Discord);
        NotificationDeliveryId first = bench.Waiting(unwritten, DeliveryBench.Began).Id;
        NotificationDelivery second = bench.Waiting(other, DeliveryBench.Began);
        bench.Deliveries.FailingSaves = 1;

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(2, 0, 0), moved);
        Assert.Equal(NotificationDeliveryState.Queued, bench.Deliveries.Held(first).State);
        Assert.Equal(NotificationDeliveryState.Sent, second.State);
        Assert.Equal([SlackPath, DiscordPath], bench.Paths);
        Assert.Equal(1, bench.Unwritten.Count);

        bench.Clock.Turn(TimeSpan.FromSeconds(5));
        NotificationDeliveries later = await bench.PassAsync();

        Assert.Equal(NotificationDeliveries.None, later);
        Assert.Equal(NotificationDeliveryState.Sent, bench.Deliveries.Held(first).State);
        Assert.Equal(DeliveryBench.Began, bench.Deliveries.Held(first).SentAt);
        Assert.Equal([SlackPath, DiscordPath], bench.Paths);
        Assert.Equal(0, bench.Unwritten.Count);
    }

    [Fact(DisplayName = "BR-NS-001: a pass that no longer remembers a notice it could not write as sent sends it once more")]
    public async Task APassThatForgotSendsOnceMore()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        NotificationDeliveryId delivery = bench.Waiting(bench.Destination(WebhookRig.Slack)).Id;
        bench.Deliveries.FailingSaves = 1;
        await bench.PassAsync();

        NotificationDeliveries moved = await bench.RoundThatRemembersNothing().SendAsync(CancellationToken.None);

        Assert.Equal(new NotificationDeliveries(1, 0, 0), moved);
        Assert.Equal(NotificationDeliveryState.Sent, bench.Deliveries.Held(delivery).State);
        Assert.Equal([SlackPath, SlackPath], bench.Paths);
    }

    [Fact(DisplayName = "BR-NS-001: a notice that still cannot be written as sent stays remembered and is still not sent again")]
    public async Task ANoticeThatStillCannotBeWrittenStaysRemembered()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        NotificationDeliveryId delivery = bench.Waiting(bench.Destination(WebhookRig.Slack)).Id;
        bench.Deliveries.FailingSaves = 2;
        await bench.PassAsync();

        await bench.PassAsync();

        Assert.Equal(NotificationDeliveryState.Queued, bench.Deliveries.Held(delivery).State);
        Assert.Equal(1, bench.Unwritten.Count);

        await bench.PassAsync();

        Assert.Equal(NotificationDeliveryState.Sent, bench.Deliveries.Held(delivery).State);
        Assert.Equal([SlackPath], bench.Paths);
        Assert.Equal(0, bench.Unwritten.Count);
    }

    [Fact(DisplayName = "BR-NS-001: a notice that was taken is forgotten once its delivery is no longer held")]
    public async Task ANoticeTakenIsForgottenOnceItsDeliveryIsGone()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        NotificationDestination destination = bench.Destination(WebhookRig.Slack);
        bench.Waiting(destination);
        bench.Deliveries.FailingSaves = 1;
        await bench.PassAsync();
        bench.Deliveries.Rows.Clear();
        bench.Waiting(destination);

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(1, 0, 0), moved);
        Assert.Equal(0, bench.Unwritten.Count);
    }

    [Fact(DisplayName = "BR-NS-001: a notice given up that could not be written so does not keep the rest of the pass from going")]
    public async Task AGivingUpThatCouldNotBeWrittenDoesNotStopThePass()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        NotificationDeliveryId tooOld = bench.Waiting(bench.Destination(WebhookRig.Slack), DeliveryBench.Began.AddHours(-2)).Id;
        NotificationDelivery fresh = bench.Waiting(bench.Destination(WebhookRig.Discord));
        bench.Deliveries.FailingSaves = 1;

        NotificationDeliveries moved = await bench.PassAsync();

        Assert.Equal(new NotificationDeliveries(1, 0, 0), moved);
        Assert.Equal(NotificationDeliveryState.Queued, bench.Deliveries.Held(tooOld).State);
        Assert.Equal(NotificationDeliveryState.Sent, fresh.State);
        Assert.Equal(0, bench.Unwritten.Count);

        Assert.Equal(new NotificationDeliveries(0, 0, 1), await bench.PassAsync());
        Assert.Equal(NotificationDeliveryState.GivenUp, bench.Deliveries.Held(tooOld).State);
        Assert.Equal([DiscordPath], bench.Paths);
    }

    [Fact(DisplayName = "BR-NA-003: what is said of a notice that could not be written carries no part of the address")]
    public async Task WhatIsSaidOfAnUnwrittenNoticeCarriesNoAddress()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        NotificationDeliveryId delivery = bench.Waiting(bench.Destination(WebhookRig.Slack)).Id;
        bench.Deliveries.FailingSaves = 1;

        await bench.PassAsync();

        string said = Assert.Single(bench.Said.Said);
        Assert.StartsWith("Error ", said, StringComparison.Ordinal);
        Assert.Contains(delivery.Value.ToString(), said, StringComparison.Ordinal);
        Assert.DoesNotContain("made-up", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hooks.slack.com", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/services/", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "BR-NS-001: a pass called off while it writes is called off, and is not taken for a write that failed")]
    public async Task APassCalledOffWhileItWritesIsCalledOff()
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(200, "ok"));
        bench.Waiting(bench.Destination(WebhookRig.Slack));
        using CancellationTokenSource calledOff = new();
        bench.Deliveries.BeforeSave = calledOff.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bench.Round.SendAsync(calledOff.Token));

        Assert.Empty(bench.Said.Said);
    }

    [Theory(DisplayName = "BR-NA-003: whatever a webhook answers, no line of the log carries the address or any part of it")]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task NoLineOfTheLogCarriesTheAddress(int status)
    {
        await using DeliveryBench bench = new(_ => CannedAnswer.Text(status, "a body nobody keeps: made-up-secret-body"));
        bench.Waiting(bench.Destination(WebhookRig.Slack));
        bench.Waiting(bench.SealedWithKeysNowLost(WebhookRig.Discord));

        await bench.PassAsync();

        Assert.NotEmpty(bench.Said.Said);
        Assert.DoesNotContain(bench.Said.Said, line => line.Contains("made-up", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(bench.Said.Said, line => line.Contains("hooks.slack.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(bench.Said.Said, line => line.Contains("discord.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(bench.Said.Said, line => line.Contains("/services/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(bench.Said.Said, line => line.Contains("wxyz", StringComparison.OrdinalIgnoreCase));
    }

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
    public async Task AnotherRejectionOfAPushServiceGivesUpOnlyThatNotice(int status)
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
    public async Task AfterAServerFailureOfAPushServiceTheNoticeWaitsThirtySeconds()
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
