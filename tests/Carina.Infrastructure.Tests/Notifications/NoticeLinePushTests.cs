using System.Net;
using System.Security.Cryptography;
using System.Text;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Tests.DataBroadcast;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Notifications;

public sealed class NoticeLinePushTests : IDisposable
{
    private const string Token = "vapid t=made-up.token.parts, k=made-up-key";

    private static readonly byte[] Content = Encoding.UTF8.GetBytes("""{"title":"録画が終わりました","body":"作り物の番組","path":"/recordings","id":null}""");

    private readonly PushReceiver receiver = new();

    [Fact(DisplayName = "RFC 8030 section 5: a message is posted over TLS to the endpoint of the subscription, encrypted as aes128gcm for that browser, and a 201 is taken")]
    public async Task AMessageIsPostedEncryptedToTheEndpoint()
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Of(201, []));

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Browser(receiver), Message(PushHandling.Usual), CancellationToken.None);

        HeardRequest heard = Assert.Single(rig.Server.Heard);
        Assert.Equal(NoticeAnswer.Taken, answer);
        Assert.Equal("POST", heard.Method);
        Assert.Equal("/fcm/send/made-up-registration", heard.Target);
        Assert.Equal("aes128gcm", heard.Headers["Content-Encoding"]);
        Assert.Equal("application/octet-stream", heard.Headers["Content-Type"]);
        Assert.Equal(Token, heard.Headers["Authorization"]);
        Assert.Equal("Carina/0.0.0-test", heard.Headers["User-Agent"]);
        Assert.False(heard.Headers.ContainsKey("Cookie"));
        Assert.Equal(Content, receiver.Open(heard.Body));
        Assert.Equal(new IPEndPoint(WebhookRig.Public, 443), Assert.Single(rig.Network.Connected));
    }

    [Fact(DisplayName = "RFC 8291 section 4: what goes over the line carries no part of the message in the clear, and two sends of one message differ")]
    public async Task WhatGoesOverTheLineIsNotInTheClear()
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Of(201, []));
        PushSubscription subscription = WebhookRig.Browser(receiver);

        await rig.Line.SendAsync(subscription, Message(PushHandling.Usual), CancellationToken.None);
        await rig.Line.SendAsync(subscription, Message(PushHandling.Usual), CancellationToken.None);

        HeardRequest[] heard = [.. rig.Server.Heard];
        Assert.Equal(2, heard.Length);
        Assert.DoesNotContain("recordings", Encoding.Latin1.GetString(heard[0].Body), StringComparison.Ordinal);
        Assert.DoesNotContain("title", Encoding.Latin1.GetString(heard[0].Body), StringComparison.Ordinal);
        Assert.NotEqual(heard[0].Body, heard[1].Body);
        Assert.Equal(Content, receiver.Open(heard[1].Body));
    }

    [Fact(DisplayName = "RFC 8291 section 4: a browser with other keys cannot open the message")]
    public async Task ABrowserWithOtherKeysCannotOpenTheMessage()
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Of(201, []));
        using PushReceiver another = new();

        await rig.Line.SendAsync(WebhookRig.Browser(receiver), Message(PushHandling.Usual), CancellationToken.None);

        Assert.ThrowsAny<CryptographicException>(() => another.Open(Assert.Single(rig.Server.Heard).Body));
    }

    [Theory(DisplayName = "D-11: the push service is told how long to keep the message, in whole seconds, and how urgent it is")]
    [InlineData(86_400_000, PushUrgency.Normal, "86400", "normal")]
    [InlineData(1_800_900, PushUrgency.High, "1800", "high")]
    [InlineData(0, PushUrgency.High, "0", "high")]
    public async Task ThePushServiceIsToldTheLifetimeAndTheUrgency(int milliseconds, PushUrgency urgency, string lifetime, string named)
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Of(201, []));
        PushHandling handling = new(TimeSpan.FromMilliseconds(milliseconds), urgency);

        await rig.Line.SendAsync(WebhookRig.Browser(receiver), Message(handling), CancellationToken.None);

        HeardRequest heard = Assert.Single(rig.Server.Heard);
        Assert.Equal(lifetime, heard.Headers["TTL"]);
        Assert.Equal(named, heard.Headers["Urgency"]);
    }

    [Theory(DisplayName = "D-11: a 200, a 201, a 202 and a 204 are taken")]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(202)]
    [InlineData(204)]
    public async Task AnyStatusInThe200sIsTaken(int status)
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Of(status, []));

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Browser(receiver), Message(PushHandling.Usual), CancellationToken.None);

        Assert.Equal(NoticeAnswer.Taken, answer);
    }

    [Theory(DisplayName = "D-11: a 404 and a 410 are a rejection that says the subscription is gone")]
    [InlineData(404)]
    [InlineData(410)]
    public async Task ASubscriptionThePushServiceNoLongerKnowsIsGone(int status)
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Text(status, "gone"));

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Browser(receiver), Message(PushHandling.Usual), CancellationToken.None);

        Assert.Equal(new NoticeAnswer(NoticeFailure.Rejected, true, null), answer);
    }

    [Theory(DisplayName = "D-11: a 401, a 403, any other 4xx and a redirect are a rejection that leaves the subscription as it is, and a redirect is not followed")]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(413)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task AnyOtherRejectionLeavesTheSubscriptionAsItIs(int status)
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Of(status, [], "Location: https://fcm.googleapis.com/fcm/send/elsewhere"));

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Browser(receiver), Message(PushHandling.Usual), CancellationToken.None);

        Assert.Equal(NoticeAnswer.Failed(NoticeFailure.Rejected), answer);
        Assert.Single(rig.Server.Heard);
    }

    [Fact(DisplayName = "BR-NC-001: a 429 from a push service says how long it asked to be left alone")]
    public async Task A429SaysHowLongThePushServiceAsked()
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Of(429, [], "Retry-After: 45"));

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Browser(receiver), Message(PushHandling.Usual), CancellationToken.None);

        Assert.Equal(new NoticeAnswer(NoticeFailure.RateLimited, false, TimeSpan.FromSeconds(45)), answer);
    }

    [Theory(DisplayName = "BR-NS-001: a 5xx from a push service is a failure of its server")]
    [InlineData(500)]
    [InlineData(503)]
    public async Task A5xxIsAFailureOfThePushService(int status)
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Text(status, "busy"));

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Browser(receiver), Message(PushHandling.Usual), CancellationToken.None);

        Assert.Equal(NoticeAnswer.Failed(NoticeFailure.ServerFailed), answer);
    }

    [Theory(DisplayName = "D-8: a push service whose name resolves to an address that is not public is not connected to")]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.7")]
    [InlineData("192.168.1.20")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("fd00::7")]
    [InlineData("::ffff:10.0.0.7")]
    public async Task APushServiceAtAnAddressThatIsNotPublicIsNotConnectedTo(string address)
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Of(201, []));
        rig.Network.Names[WebhookRig.PushHost] = [WebhookRig.Public, IPAddress.Parse(address)];

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Browser(receiver), Message(PushHandling.Usual), CancellationToken.None);

        Assert.Equal(NoticeAnswer.Failed(NoticeFailure.Unreachable), answer);
        Assert.Empty(rig.Network.Connected);
        Assert.Empty(rig.Server.Heard);
    }

    [Fact(DisplayName = "D-8: a push service that shows a certificate nobody vouches for is a failure of TLS, and nothing is posted")]
    public async Task ACertificateNobodyVouchesForIsAFailureOfTls()
    {
        await using WebhookRig rig = new(_ => CannedAnswer.Of(201, []));
        rig.Network.Trusted = null;

        NoticeAnswer answer = await rig.Line.SendAsync(WebhookRig.Browser(receiver), Message(PushHandling.Usual), CancellationToken.None);

        Assert.Equal(NoticeAnswer.Failed(NoticeFailure.TlsFailed), answer);
        Assert.Empty(rig.Server.Heard);
    }

    public void Dispose() => receiver.Dispose();

    private static PushMessage Message(PushHandling handling) => new(Content, Token, handling);
}
