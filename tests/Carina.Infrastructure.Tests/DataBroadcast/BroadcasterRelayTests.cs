using System.Net;
using System.Text;

using Carina.Broadcast.DsmCc;
using Carina.Domain.DataBroadcast;
using Carina.Infrastructure.DataBroadcast;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.DataBroadcast;

public sealed class BroadcasterRelayTests
{
    private const string Channel = "32736-1024";

    private const string Host = "station.example";

    private static readonly IPAddress Public = IPAddress.Parse("203.0.113.7");

    [Fact(DisplayName = "BR-BD-011: an empty text asks with GET and sends no body")]
    public async Task AnEmptyTextAsksWithGet()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "1"));

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/check/bc.txt", string.Empty, "EUC-JP", CancellationToken.None);

        HeardRequest heard = Assert.Single(rig.Server.Heard);
        Assert.Equal(new RelayedText(RelayResult.Answered, 200, "1"), relayed);
        Assert.Equal("GET", heard.Method);
        Assert.Equal("/check/bc.txt", heard.Target);
        Assert.Empty(heard.Body);
        Assert.False(heard.Headers.ContainsKey("Content-Type"));
    }

    [Fact(DisplayName = "BR-BD-011: a text is sent with POST as a form in EUC-JP, and the answer comes back from EUC-JP")]
    public async Task ATextIsSentWithPostInEucJpAndTheAnswerComesBack()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Of(200, EucJpText.Encode("受付\t完了"), "Content-Type: text/plain; charset=EUC-JP"));

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/entry", "応募\t1", "EUC-JP", CancellationToken.None);

        HeardRequest heard = Assert.Single(rig.Server.Heard);
        Assert.Equal("POST", heard.Method);
        Assert.Equal("application/x-www-form-urlencoded; charset=EUC-JP", heard.Headers["Content-Type"]);
        Assert.Equal(EucJpText.Encode("応募\t1"), heard.Body);
        Assert.Equal(new RelayedText(RelayResult.Answered, 200, "受付\t完了"), relayed);
    }

    [Fact(DisplayName = "BR-BD-011: an answer that names no character set is read in the one the document asked in")]
    public async Task AnAnswerThatNamesNoCharacterSetIsReadInTheOneAsked()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Of(200, EucJpText.Encode("天気")));

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/", string.Empty, "EUC-JP", CancellationToken.None);

        Assert.Equal("天気", relayed.Body);
    }

    [Fact(DisplayName = "BR-BD-011: the relay introduces itself by its own name and version, over HTTP/1.1")]
    public async Task TheRelayIntroducesItselfByItsOwnName()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "1"));

        await rig.Relay.TransmitAsync(Channel, $"http://{Host}/", string.Empty, "EUC-JP", CancellationToken.None);

        HeardRequest heard = Assert.Single(rig.Server.Heard);
        Assert.StartsWith("Carina/", heard.Headers["User-Agent"], StringComparison.Ordinal);
        Assert.Equal(BroadcasterRelaySettings.Default.UserAgent, heard.Headers["User-Agent"]);
        Assert.Equal(Host, heard.Headers["Host"]);
        Assert.False(heard.Headers.ContainsKey("Cookie"));
    }

    [Theory(DisplayName = "BR-BD-013: a server that answers 4xx or 5xx is a result of 1 with its status and body")]
    [InlineData(404)]
    [InlineData(403)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task AServerThatAnswersAnErrorIsAResultOfOneWithItsStatus(int status)
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(status, "no"));

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/", "x", "EUC-JP", CancellationToken.None);

        Assert.Equal(new RelayedText(RelayResult.Answered, status, "no"), relayed);
    }

    [Fact(DisplayName = "BR-BD-013: a name that does not resolve is -400, and nothing is connected to")]
    public async Task ANameThatDoesNotResolveIsMinus400()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "1"));

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, "http://nowhere.example/", string.Empty, "EUC-JP", CancellationToken.None);

        Assert.Equal(RelayedText.Failed(RelayResult.NameNotResolved), relayed);
        Assert.Empty(rig.Network.Connected);
    }

    [Fact(DisplayName = "BR-BD-013: a connection the server refuses is -2")]
    public async Task AConnectionTheServerRefusesIsMinus2()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "1"));
        rig.Network.Refuses = true;

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/", string.Empty, "EUC-JP", CancellationToken.None);

        Assert.Equal(RelayedText.Failed(RelayResult.Disconnected), relayed);
    }

    [Fact(DisplayName = "BR-BD-011: a server that does not answer in time is -3")]
    public async Task AServerThatDoesNotAnswerInTimeIsMinus3()
    {
        TaskCompletionSource<CannedAnswer> never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using RelayRig rig = new(_ => never.Task, settings => settings with { Time = TimeSpan.FromMilliseconds(400) });

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/", string.Empty, "EUC-JP", CancellationToken.None);

        Assert.Equal(RelayedText.Failed(RelayResult.TimedOut), relayed);
        Assert.Single(rig.Server.Heard);
    }

    [Fact(DisplayName = "BR-BD-013: a server whose certificate this machine does not trust is -500")]
    public async Task AServerWhoseCertificateIsNotTrustedIsMinus500()
    {
        using System.Security.Cryptography.X509Certificates.X509Certificate2 own = BroadcasterServerStandIn.SelfSigned(Host);
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "1"), certificate: own);

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, $"https://{Host}/v3/token", "id", "EUC-JP", CancellationToken.None);

        Assert.Equal(RelayedText.Failed(RelayResult.SecureConnectionFailed), relayed);
        Assert.Empty(rig.Server.Heard);
    }

    [Theory(DisplayName = "BR-BD-011: an address that is not http or https on port 80 or 443 to a public host is -1, and nothing is resolved or connected to")]
    [InlineData("ftp://station.example/file")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://station.example:8080/")]
    [InlineData("https://station.example:8443/")]
    [InlineData("http://user:secret@station.example/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://172.16.4.2/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://2130706433/")]
    [InlineData("not an address")]
    [InlineData("/relative/path")]
    [InlineData("")]
    public async Task AnAddressOutsideTheRangeIsMinus1(string uri)
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "1"));

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, uri, string.Empty, "EUC-JP", CancellationToken.None);

        Assert.Equal(RelayedText.Failed(RelayResult.RefusedOrTooLarge), relayed);
        Assert.Empty(rig.Network.Resolved);
        Assert.Empty(rig.Network.Connected);
    }

    [Theory(DisplayName = "BR-BD-011: a name that resolves to an address inside is -1, judged as the connection opens, and nothing is connected to")]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("192.168.0.10")]
    [InlineData("100.64.0.1")]
    [InlineData("::1")]
    [InlineData("fc00::5")]
    [InlineData("::ffff:10.0.0.1")]
    public async Task ANameThatResolvesInsideIsMinus1(string inside)
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "1"));
        rig.Network.Names["inward.example"] = [IPAddress.Parse(inside)];

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, "http://inward.example/", string.Empty, "EUC-JP", CancellationToken.None);

        Assert.Equal(RelayedText.Failed(RelayResult.RefusedOrTooLarge), relayed);
        Assert.Equal(["inward.example"], rig.Network.Resolved);
        Assert.Empty(rig.Network.Connected);
    }

    [Fact(DisplayName = "BR-BD-011: a name pointed inwards after it was first answered is refused the next time it is asked for")]
    public async Task ANamePointedInwardsLaterIsRefused()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "1"), settings => settings);

        RelayedText first = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/", string.Empty, "EUC-JP", CancellationToken.None);
        rig.Network.Names[Host] = [IPAddress.Parse("10.0.0.9")];
        RelayedText second = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/again", string.Empty, "EUC-JP", CancellationToken.None);

        Assert.Equal(RelayResult.Answered, first.Result);
        Assert.Equal(RelayedText.Failed(RelayResult.RefusedOrTooLarge), second);
        Assert.Single(rig.Server.Heard);
    }

    [Fact(DisplayName = "BR-BD-011: a name with a public address and one inside is refused as a whole")]
    public async Task ANameWithOneAddressInsideIsRefused()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "1"));
        rig.Network.Names["mixed.example"] = [Public, IPAddress.Parse("192.168.0.2")];

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, "http://mixed.example/", string.Empty, "EUC-JP", CancellationToken.None);

        Assert.Equal(RelayResult.RefusedOrTooLarge, relayed.Result);
        Assert.Empty(rig.Network.Connected);
    }

    [Fact(DisplayName = "BR-BD-011: a text of 16 KiB is sent, and one byte more is -1 and never leaves")]
    public async Task ATextOverSixteenKibIsMinus1()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "1"));
        string most = new('a', 16 * 1024);

        RelayedText within = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/", most, "EUC-JP", CancellationToken.None);
        RelayedText over = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/", most + "a", "EUC-JP", CancellationToken.None);

        Assert.Equal(RelayResult.Answered, within.Result);
        Assert.Equal(RelayedText.Failed(RelayResult.RefusedOrTooLarge), over);
        Assert.Equal(16 * 1024, Assert.Single(rig.Server.Heard).Body.Length);
    }

    [Theory(DisplayName = "BR-BD-011: an answer over 256 KiB is -1, whether its length is said or not")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnAnswerOverTheLimitIsMinus1(bool measured)
    {
        byte[] large = new byte[(256 * 1024) + 1];
        Array.Fill(large, (byte)'a');
        await using RelayRig rig = new(_ => new CannedAnswer(200, large, [], measured));

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/", string.Empty, "EUC-JP", CancellationToken.None);

        Assert.Equal(RelayedText.Failed(RelayResult.RefusedOrTooLarge), relayed);
    }

    [Fact(DisplayName = "BR-BD-011: an answer of exactly 256 KiB is taken back whole")]
    public async Task AnAnswerAtTheLimitIsTakenBack()
    {
        byte[] most = new byte[256 * 1024];
        Array.Fill(most, (byte)'a');
        await using RelayRig rig = new(_ => new CannedAnswer(200, most, [], Measured: false));

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/", string.Empty, "EUC-JP", CancellationToken.None);

        Assert.Equal(RelayResult.Answered, relayed.Result);
        Assert.Equal(256 * 1024, relayed.Body!.Length);
    }

    [Theory(DisplayName = "BR-BD-011: a character set other than EUC-JP or UTF-8 is -1")]
    [InlineData("Shift_JIS")]
    [InlineData("ISO-2022-JP")]
    [InlineData("")]
    public async Task ACharacterSetOtherThanTheTwoIsMinus1(string charset)
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "1"));

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/", "x", charset, CancellationToken.None);

        Assert.Equal(RelayedText.Failed(RelayResult.RefusedOrTooLarge), relayed);
        Assert.Empty(rig.Server.Heard);
    }

    [Fact(DisplayName = "BR-BD-011: a text asked in UTF-8 is sent in UTF-8")]
    public async Task ATextAskedInUtf8IsSentInUtf8()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Of(200, Encoding.UTF8.GetBytes("済"), "Content-Type: text/plain; charset=utf-8"));

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/", "投票", "UTF-8", CancellationToken.None);

        HeardRequest heard = Assert.Single(rig.Server.Heard);
        Assert.Equal(Encoding.UTF8.GetBytes("投票"), heard.Body);
        Assert.Equal("application/x-www-form-urlencoded; charset=UTF-8", heard.Headers["Content-Type"]);
        Assert.Equal("済", relayed.Body);
    }

    [Fact(DisplayName = "BR-BD-011: a redirect on the same host is followed, a POST turned to a GET on 302")]
    public async Task ARedirectOnTheSameHostIsFollowed()
    {
        await using RelayRig rig = new(request => request.Target == "/moved"
            ? CannedAnswer.Of(302, [], "Location: /landed")
            : CannedAnswer.Text(200, "here"));

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/moved", "x", "EUC-JP", CancellationToken.None);

        Assert.Equal(new RelayedText(RelayResult.Answered, 200, "here"), relayed);
        Assert.Equal(["POST /moved", "GET /landed"], rig.Server.Heard.Select(heard => $"{heard.Method} {heard.Target}"));
    }

    [Fact(DisplayName = "BR-BD-011: a 307 keeps the method and the body it was sent with")]
    public async Task A307KeepsTheMethodAndTheBody()
    {
        await using RelayRig rig = new(request => request.Target == "/moved"
            ? CannedAnswer.Of(307, [], "Location: /landed")
            : CannedAnswer.Text(200, "here"));

        await rig.Relay.TransmitAsync(Channel, $"http://{Host}/moved", "x", "EUC-JP", CancellationToken.None);

        Assert.Equal(["POST /moved x", "POST /landed x"], rig.Server.Heard.Select(heard => $"{heard.Method} {heard.Target} {Encoding.ASCII.GetString(heard.Body)}"));
    }

    [Theory(DisplayName = "BR-BD-011: a redirect to another host, or to an address inside, is not followed and is answered as it came")]
    [InlineData("http://elsewhere.example/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://station.example:8080/")]
    [InlineData("ftp://station.example/")]
    public async Task ARedirectAwayIsNotFollowed(string location)
    {
        await using RelayRig rig = new(_ => CannedAnswer.Of(302, [], $"Location: {location}"));

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/", string.Empty, "EUC-JP", CancellationToken.None);

        Assert.Equal(new RelayedText(RelayResult.Answered, 302, string.Empty), relayed);
        Assert.Single(rig.Server.Heard);
    }

    [Fact(DisplayName = "BR-BD-011: redirects are followed three times, and the fourth is answered as it came")]
    public async Task RedirectsAreFollowedThreeTimes()
    {
        int asked = 0;
        await using RelayRig rig = new(_ => CannedAnswer.Of(302, [], $"Location: /hop{Interlocked.Increment(ref asked)}"));

        RelayedText relayed = await rig.Relay.TransmitAsync(Channel, $"http://{Host}/", string.Empty, "EUC-JP", CancellationToken.None);

        Assert.Equal(302, relayed.HttpStatus);
        Assert.Equal(4, rig.Server.Heard.Count);
    }

    [Fact(DisplayName = "BR-BD-011: a channel has two exchanges under way at once, and a third waits for one of them")]
    public async Task AThirdExchangeWaits()
    {
        TaskCompletionSource<CannedAnswer> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using RelayRig rig = new(_ => gate.Task);

        Task<RelayedText>[] sent =
        [
            .. Enumerable.Range(0, 3).Select(index =>
                rig.Relay.TransmitAsync(Channel, $"http://{Host}/{index}", string.Empty, "EUC-JP", CancellationToken.None)),
        ];
        await Eventually.Happens(() => rig.Server.Heard.Count == 2, "two exchanges reach the server");
        await Task.Delay(300);
        int heardBeforeOneEnded = rig.Server.Heard.Count;
        Task<RelayedText> otherChannel = rig.Relay.TransmitAsync("32736-1025", $"http://{Host}/other", string.Empty, "EUC-JP", CancellationToken.None);
        await Eventually.Happens(() => rig.Server.Heard.Count == 3, "another channel is not held up by this one");
        gate.SetResult(CannedAnswer.Text(200, "1"));
        RelayedText[] answered = await Task.WhenAll([.. sent, otherChannel]);

        Assert.Equal(2, heardBeforeOneEnded);
        Assert.All(answered, relayed => Assert.Equal(RelayResult.Answered, relayed.Result));
        Assert.Equal(4, rig.Server.Heard.Count);
    }

    [Fact(DisplayName = "BR-BD-012: an exchange is written down by channel, host, path, method, result, status, sizes and time, and never by its query or bodies")]
    public async Task AnExchangeIsWrittenDownWithoutItsQueryOrBodies()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "token-answered"));

        await rig.Relay.TransmitAsync(Channel, $"http://{Host}/v3/dtv/create?terminal_id=0123456789ab", "card=0123456789ab", "EUC-JP", CancellationToken.None);

        KeptLine line = Assert.Single(rig.Log.Lines);
        Assert.Equal(Channel, line.Values["Channel"]);
        Assert.Equal("POST", line.Values["Method"]);
        Assert.Equal(Host, line.Values["Host"]);
        Assert.Equal("/v3/dtv/create", line.Values["Path"]);
        Assert.Equal(1, line.Values["Result"]);
        Assert.Equal(200, line.Values["Status"]);
        Assert.Equal(17, line.Values["Sent"]);
        Assert.Equal(14, line.Values["Received"]);
        Assert.True(line.Values.ContainsKey("Elapsed"));
        Assert.DoesNotContain("0123456789ab", line.Said, StringComparison.Ordinal);
        Assert.DoesNotContain("terminal_id", line.Said, StringComparison.Ordinal);
        Assert.DoesNotContain("token-answered", line.Said, StringComparison.Ordinal);
        Assert.DoesNotContain(line.Values.Values, value => value?.ToString()?.Contains("0123456789ab", StringComparison.Ordinal) is true);
    }

    [Fact(DisplayName = "BR-BD-012: a refused exchange is written down too")]
    public async Task ARefusedExchangeIsWrittenDownToo()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "1"));

        await rig.Relay.TransmitAsync(Channel, "http://10.0.0.1/inside?secret=1", string.Empty, "EUC-JP", CancellationToken.None);

        KeptLine line = Assert.Single(rig.Log.Lines);
        Assert.Equal(-1, line.Values["Result"]);
        Assert.Null(line.Values["Status"]);
        Assert.Equal("/inside", line.Values["Path"]);
        Assert.DoesNotContain("secret", line.Said, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-BD-011: a BML document is fetched as a resource placed at its address, its EUC-JP text in UTF-8")]
    public async Task ABmlDocumentIsFetchedAsAResource()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Of(200, EucJpText.Encode("<bml>天気</bml>"), "Content-Type: text/X-arib-bml; charset=EUC-JP"));

        RelayedResource fetched = await rig.Relay.FetchAsync(Channel, $"http://{Host}/bmltop/startup.bml", CancellationToken.None);

        Assert.Equal(RelayResult.Answered, fetched.Result);
        Assert.Equal(200, fetched.HttpStatus);
        CarouselResource resource = fetched.Resource!;
        Assert.Equal($"http://{Host}/bmltop/startup.bml", resource.Path);
        Assert.Equal("text/X-arib-bml", resource.MediaType);
        Assert.Equal(ResourceForm.Text, resource.Form);
        Assert.Equal("<bml>天気</bml>", Encoding.UTF8.GetString(resource.Body.Span));
        Assert.Equal("GET", Assert.Single(rig.Server.Heard).Method);
    }

    [Fact(DisplayName = "BR-BD-011: a fetched resource the server types loosely is typed by its extension, and placed where a redirect landed")]
    public async Task AFetchedResourceIsTypedByItsExtension()
    {
        await using RelayRig rig = new(request => request.Target == "/img/logo"
            ? CannedAnswer.Of(301, [], "Location: /img/logo.png")
            : CannedAnswer.Of(200, [0x89, 0x50, 0x4E, 0x47], "Content-Type: application/octet-stream"));

        RelayedResource fetched = await rig.Relay.FetchAsync(Channel, $"http://{Host}/img/logo", CancellationToken.None);

        CarouselResource resource = fetched.Resource!;
        Assert.Equal($"http://{Host}/img/logo.png", resource.Path);
        Assert.Equal("image/X-arib-png", resource.MediaType);
        Assert.Equal(ResourceForm.Binary, resource.Form);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, resource.Body.ToArray());
    }

    [Fact(DisplayName = "BR-BD-013: a fetch the server answers with 404 carries its status and no resource")]
    public async Task AFetchAnsweredWith404CarriesNoResource()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(404, "gone"));

        RelayedResource fetched = await rig.Relay.FetchAsync(Channel, $"http://{Host}/gone.bml", CancellationToken.None);

        Assert.Equal(new RelayedResource(RelayResult.Answered, 404, null), fetched);
    }

    [Fact(DisplayName = "BR-BD-011: a fetch of an address inside is -1 and connects to nothing")]
    public async Task AFetchOfAnAddressInsideIsMinus1()
    {
        await using RelayRig rig = new(_ => CannedAnswer.Text(200, "1"));

        RelayedResource fetched = await rig.Relay.FetchAsync(Channel, "http://192.168.0.1/startup.bml", CancellationToken.None);

        Assert.Equal(RelayedResource.Failed(RelayResult.RefusedOrTooLarge), fetched);
        Assert.Empty(rig.Network.Connected);
    }

    private sealed class RelayRig : IAsyncDisposable
    {
        public RelayRig(
            Func<HeardRequest, CannedAnswer> answer,
            Func<BroadcasterRelaySettings, BroadcasterRelaySettings>? adjust = null,
            System.Security.Cryptography.X509Certificates.X509Certificate2? certificate = null)
            : this(request => Task.FromResult(answer(request)), adjust, certificate)
        {
        }

        public RelayRig(
            Func<HeardRequest, Task<CannedAnswer>> answer,
            Func<BroadcasterRelaySettings, BroadcasterRelaySettings>? adjust = null,
            System.Security.Cryptography.X509Certificates.X509Certificate2? certificate = null)
        {
            Server = new BroadcasterServerStandIn(answer, certificate);
            Network = new PointedNetwork(Server.Port);
            Network.Names[Host] = [Public];
            Network.Names["elsewhere.example"] = [IPAddress.Parse("198.51.100.20")];
            BroadcasterRelaySettings settings = (adjust ?? (given => given))(BroadcasterRelaySettings.Default);
            Relay = new BroadcasterRelay(Network, settings, TimeProvider.System, Log);
        }

        public BroadcasterServerStandIn Server { get; }

        public PointedNetwork Network { get; }

        public KeptRelayLog Log { get; } = new();

        public BroadcasterRelay Relay { get; }

        public async ValueTask DisposeAsync()
        {
            Relay.Dispose();
            await Server.DisposeAsync();
        }
    }
}
