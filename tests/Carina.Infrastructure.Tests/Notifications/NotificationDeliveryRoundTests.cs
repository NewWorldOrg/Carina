using System.Text;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Tests.DataBroadcast;

namespace Carina.Infrastructure.Tests.Notifications;

public sealed class NotificationDeliveryRoundTests
{
    private const string SlackPath = "/services/T0000/B0000/made-up-token-wxyz";

    private const string DiscordPath = "/api/webhooks/1234/made-up-token-abcd";

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
}
