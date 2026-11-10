using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Tests.DataBroadcast;

namespace Carina.Infrastructure.Tests.Notifications;

public sealed class NoticeLineTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("""{"said":"録画が終わりました"}""");

    [Fact(DisplayName = "BR-NS-001: a notice is posted as JSON over TLS to the path of the webhook, and a 2xx is taken")]
    public async Task ANoticeIsPostedAsJsonAndA2xxIsTaken()
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Text(200, "ok"));

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Slack, Payload, CancellationToken.None);

        HeardRequest heard = Assert.Single(rig.Server.Heard);
        Assert.Equal(NoticeAnswer.Taken, answer);
        Assert.True(answer.IsTaken);
        Assert.Equal("POST", heard.Method);
        Assert.Equal("/services/T0000/B0000/made-up-token-wxyz", heard.Target);
        Assert.Equal("application/json", heard.Headers["Content-Type"]);
        Assert.Equal("Carina/0.0.0-test", heard.Headers["User-Agent"]);
        Assert.Equal(Payload, heard.Body);
        Assert.False(heard.Headers.ContainsKey("Cookie"));
        Assert.Equal(new IPEndPoint(WebhookRig.Public, 443), Assert.Single(rig.Network.Connected));
    }

    [Theory(DisplayName = "BR-NS-001: a 204 and a 201 are taken as well")]
    [InlineData(201)]
    [InlineData(204)]
    public async Task AnyStatusInThe200sIsTaken(int status)
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Of(status, []));

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Discord, Payload, CancellationToken.None);

        Assert.Equal(NoticeAnswer.Taken, answer);
    }

    [Theory(DisplayName = "BR-NS-001: a redirect is a rejection and is not followed, wherever it points")]
    [InlineData(301, "https://hooks.slack.com/services/elsewhere")]
    [InlineData(302, "http://127.0.0.1/")]
    [InlineData(303, "https://discord.com/api/webhooks/9/other")]
    [InlineData(307, "https://carina.example/api/health")]
    [InlineData(308, "/services/T0000/B0000/another")]
    public async Task ARedirectIsARejectionAndIsNotFollowed(int status, string location)
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Of(status, [], $"Location: {location}"));
        rig.Network.Names["carina.example"] = [WebhookRig.Public];

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Slack, Payload, CancellationToken.None);

        Assert.Equal(NoticeAnswer.Failed(NoticeFailure.Rejected), answer);
        Assert.Single(rig.Server.Heard);
        Assert.Single(rig.Network.Connected);
    }

    [Theory(DisplayName = "BR-NS-002: a 401 and a 404 are a rejection that says the address is gone")]
    [InlineData(401)]
    [InlineData(404)]
    public async Task A401AndA404SayTheAddressIsGone(int status)
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Text(status, "no_service"));

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Slack, Payload, CancellationToken.None);

        Assert.Equal(new NoticeAnswer(NoticeFailure.Rejected, true, null), answer);
    }

    [Theory(DisplayName = "BR-NS-001: any other 4xx is a rejection that leaves the address standing")]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(410)]
    [InlineData(413)]
    public async Task AnyOther4xxIsARejectionThatLeavesTheAddressStanding(int status)
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Text(status, "invalid_payload"));

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Slack, Payload, CancellationToken.None);

        Assert.Equal(new NoticeAnswer(NoticeFailure.Rejected, false, null), answer);
    }

    [Theory(DisplayName = "BR-NC-001: a 429 carries how long the other end asked to wait, Retry-After before X-RateLimit-Reset-After, never more than 10 minutes")]
    [InlineData("Retry-After: 7", 7.0)]
    [InlineData("X-RateLimit-Reset-After: 1.5", 1.5)]
    [InlineData("Retry-After: 7|X-RateLimit-Reset-After: 90", 7.0)]
    [InlineData("Retry-After: 86400", 600.0)]
    [InlineData("X-RateLimit-Reset-After: 1e12", 600.0)]
    public async Task A429CarriesHowLongToWait(string headers, double seconds)
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Of(429, [], headers.Split('|')));

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Discord, Payload, CancellationToken.None);

        Assert.Equal(new NoticeAnswer(NoticeFailure.RateLimited, false, TimeSpan.FromSeconds(seconds)), answer);
    }

    [Theory(DisplayName = "BR-NC-001: a 429 that names no wait, or one that cannot be read, carries none")]
    [InlineData("X-Other: 1")]
    [InlineData("X-RateLimit-Reset-After: soon")]
    [InlineData("X-RateLimit-Reset-After: -3")]
    [InlineData("X-RateLimit-Reset-After: NaN")]
    public async Task A429ThatNamesNoWaitCarriesNone(string header)
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Of(429, [], header));

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Discord, Payload, CancellationToken.None);

        Assert.Equal(new NoticeAnswer(NoticeFailure.RateLimited, false, null), answer);
    }

    [Theory(DisplayName = "BR-NS-001: a 5xx is a failure of the other end")]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task A5xxIsAFailureOfTheOtherEnd(int status)
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Text(status, "down"));

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Slack, Payload, CancellationToken.None);

        Assert.Equal(NoticeAnswer.Failed(NoticeFailure.ServerFailed), answer);
    }

    [Fact(DisplayName = "BR-NS-001: a webhook that does not answer within the deadline is timed out")]
    public async Task AWebhookThatDoesNotAnswerInTimeIsTimedOut()
    {
        TaskCompletionSource<CannedAnswer> never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using WebhookRig rig = new(_ => never.Task, TimeSpan.FromMilliseconds(400));

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Slack, Payload, CancellationToken.None);

        Assert.Equal(NoticeAnswer.Failed(NoticeFailure.TimedOut), answer);
    }

    [Fact(DisplayName = "BR-NS-001: the deadline of a notice is 10 seconds")]
    public void TheDeadlineOfANoticeIs10Seconds()
        => Assert.Equal(TimeSpan.FromSeconds(10), NoticeLine.UsualDeadline);

    [Fact(DisplayName = "BR-NS-001: a connection the other end refuses is unreachable")]
    public async Task AConnectionTheOtherEndRefusesIsUnreachable()
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Text(200, "ok"));
        rig.Network.Refuses = true;

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Slack, Payload, CancellationToken.None);

        Assert.Equal(NoticeAnswer.Failed(NoticeFailure.Unreachable), answer);
        Assert.Empty(rig.Server.Heard);
    }

    [Fact(DisplayName = "BR-NS-001: a name that does not resolve is unreachable")]
    public async Task ANameThatDoesNotResolveIsUnreachable()
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Text(200, "ok"));
        rig.Network.Names.Clear();

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Slack, Payload, CancellationToken.None);

        Assert.Equal(NoticeAnswer.Failed(NoticeFailure.Unreachable), answer);
        Assert.Empty(rig.Network.Connected);
    }

    [Theory(DisplayName = "BR-NT-001: a name that resolves to an address that is not public is not connected to")]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.4.2")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:10.20.30.40")]
    [InlineData("203.0.113.40,10.0.0.5")]
    public async Task ANameThatResolvesInwardsIsNotConnectedTo(string resolved)
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Text(200, "ok"));
        rig.Network.Names[WebhookRig.SlackHost] = [.. resolved.Split(',').Select(IPAddress.Parse)];

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Slack, Payload, CancellationToken.None);

        Assert.Equal(NoticeAnswer.Failed(NoticeFailure.Unreachable), answer);
        Assert.Empty(rig.Network.Connected);
        Assert.Empty(rig.Server.Heard);
    }

    [Fact(DisplayName = "BR-NS-001: a webhook whose certificate is not trusted fails on TLS and hears nothing")]
    public async Task AWebhookWhoseCertificateIsNotTrustedFailsOnTls()
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Text(200, "ok"));
        rig.Network.Trusted = null;

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Slack, Payload, CancellationToken.None);

        Assert.Equal(NoticeAnswer.Failed(NoticeFailure.TlsFailed), answer);
        Assert.Empty(rig.Server.Heard);
    }

    [Theory(DisplayName = "BR-NS-001: the network of the machine trusts a certificate only when nothing is wrong with it")]
    [InlineData(SslPolicyErrors.None, true)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch, false)]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors, false)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable, false)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors, false)]
    public void TheNetworkOfTheMachineTrustsOnlyACleanCertificate(SslPolicyErrors errors, bool trusted)
    {
        using X509Certificate2 shown = BroadcasterServerStandIn.SelfSigned(WebhookRig.SlackHost);

        Assert.Equal(trusted, new SystemNoticeNetwork().Trusts(shown, errors));
    }

    [Fact(DisplayName = "BR-NS-001: a send the caller calls off is called off, not timed out")]
    public async Task ASendTheCallerCallsOffIsCalledOff()
    {
        TaskCompletionSource<CannedAnswer> never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using WebhookRig rig = new(_ => never.Task);
        using CancellationTokenSource calledOff = new(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => rig.Line.SendAsync(WebhookRig.Slack, Payload, calledOff.Token));
    }
}
