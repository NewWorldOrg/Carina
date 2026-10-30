using System.Net;
using System.Net.Sockets;
using System.Text;

using Carina.Driver.CardLending;
using Carina.Driver.Descrambling;

namespace Carina.Driver.Tests;

internal sealed class InMemoryLending(CardLender lender) : HttpMessageHandler
{
    public string Connection { get; set; } = "in-memory-1";

    public List<string> Paths { get; } = [];

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using MemoryStream body = new();
        request.Content?.ReadAsStream(cancellationToken).CopyTo(body);
        string path = request.RequestUri!.AbsolutePath;
        Paths.Add(path);

        LendingReply reply = path switch
        {
            LendingProtocol.ChallengePath => new LendingReply(LendingReply.Ok, lender.Challenge(Connection)),
            LendingProtocol.LeasePath => lender.Lease(Connection, request.Headers.Host ?? string.Empty, body.ToArray()),
            LendingProtocol.EcmPath => lender.Answer(Connection, body.ToArray()),
            _ => new LendingReply(404, []),
        };

        return new HttpResponseMessage((HttpStatusCode)reply.Status) { Content = new ByteArrayContent(reply.Body) };
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) => Task.FromResult(Send(request, cancellationToken));
}

public sealed class BorrowedCardTests
{
    private const string Host = "lender.example";

    private static readonly LendingKey Key = LendingKey.FromText("a household key long enough to be a key 0123");

    private static readonly byte[] Ecm = Encoding.ASCII.GetBytes("synthetic ecm for borrowing");

    private static readonly CardLoan Loan = new(new IPEndPoint(IPAddress.Loopback, 40772), Host, 2, Key);

    private readonly CardsOnHand cards = new();

    private CardLender Lender(int leases = 2) =>
        new(Key, cards.Open, new CardLendingLimits(leases, null), TimeProvider.System);

    [Fact]
    public void ABorrowedCardAnswersWithTheKeysOfTheLendersCard()
    {
        using CardLender lender = Lender();
        using BorrowedCard card = BorrowedCard.Open(Loan, new InMemoryLending(lender));

        EcmAnswer answer = card.Answer(Ecm);

        Assert.True(answer.Granted);
        Assert.Equal(
            [.. SyntheticCardKeys.SystemKey, .. SyntheticCardKeys.InitialValue, .. SyntheticCardKeys.PairFor(Ecm)],
            answer.KeyMaterial.ToArray()
        );
        Assert.Equal(SyntheticCardKeys.CaSystemId, card.CaSystemId);
    }

    [Fact]
    public void ABorrowedCardLeasesAgainWhenTheLenderNoLongerKnowsItsConnection()
    {
        using CardLender lender = Lender(leases: 1);
        InMemoryLending lending = new(lender);
        using BorrowedCard card = BorrowedCard.Open(Loan, lending);
        card.Answer(Ecm);
        lender.Release(lending.Connection);
        lending.Connection = "in-memory-2";

        EcmAnswer answer = card.Answer(Ecm);

        Assert.True(answer.Granted);
        Assert.Equal(1, lender.LeasesOut);
    }

    [Fact]
    public void ARefusalByTheLendersCardIsPassedOn()
    {
        FakeSmartCardService refusing = FakeSmartCardService.WithOneCard(out FakeCardConnection lent);
        lent.EcmReturnCode = _ => 0x8901;
        using CardLender lender = new(Key, () => ConditionalAccessCard.Open(refusing), new CardLendingLimits(1, null), TimeProvider.System);
        using BorrowedCard card = BorrowedCard.Open(Loan, new InMemoryLending(lender));

        EcmAnswer answer = card.Answer(Ecm);

        Assert.False(answer.Granted);
        Assert.Equal(0x8901, answer.ReturnCode);
        Assert.Equal(0, card.Granted);
        Assert.Equal(1, card.Refused);
    }

    [Fact]
    public void ALenderWithNoLeaseToSpareIsTheCardNotAnsweringAndSaysWhy()
    {
        using CardLender lender = Lender(leases: 1);
        using BorrowedCard first = BorrowedCard.Open(Loan, new InMemoryLending(lender) { Connection = "first" });

        DescramblingException refused = Assert.Throws<DescramblingException>(
            () => BorrowedCard.Open(Loan, new InMemoryLending(lender) { Connection = "second" })
        );

        Assert.Contains("busy", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABorrowerWithAnotherKeyIsTheCardNotAnswering()
    {
        using CardLender lender = Lender();
        CardLoan wrong = Loan with { Key = LendingKey.FromText("another household key that is not the same one") };

        DescramblingException refused = Assert.Throws<DescramblingException>(
            () => BorrowedCard.Open(wrong, new InMemoryLending(lender))
        );

        Assert.Contains("proof", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALenderThatLostItsCardIsTheCardNotAnswering()
    {
        using CardLender lender = Lender();
        using BorrowedCard card = BorrowedCard.Open(Loan, new InMemoryLending(lender));
        cards.Connections.Single().Failures.Enqueue(SmartCardCodes.NoService);

        Assert.Throws<DescramblingException>(() => card.Answer(Ecm));
    }

    [Fact]
    public void BorrowedCardsOpenADescramblerPerTuner()
    {
        using CardLender lender = Lender();
        BorrowedCards cards = BorrowedCards.Over(() => BorrowedCard.Open(Loan, new InMemoryLending(lender)), logger: null);
        SyntheticScrambledStream stream = new SyntheticScrambledStream()
            .Pat((1, 0x0101))
            .Pmt(0x0101, 1, 0x0901, [(0x0111, null)])
            .Ecm(0x0901, Ecm)
            .Scrambled(0x0111, Ecm, odd: true, seed: 1)
            .Scrambled(0x0111, Ecm, odd: false, seed: 2);

        using IDescrambler? descrambler = cards.Open(HeadHold.EveryProgramme);

        Assert.True(cards.Unscrambles);
        Assert.Null(cards.CardInHand);
        Assert.Equal(stream.Output, descrambler!.Descramble(stream.Input));
    }

    [Fact]
    public void BorrowedCardsThatCannotReachTheLenderOpenNothing()
    {
        BorrowedCards cards = BorrowedCards.Over(
            () => throw new DescramblingException("The lender did not answer."),
            logger: null
        );

        Assert.Null(cards.Open(HeadHold.EveryProgramme));
    }
}

public sealed class CardLendingOverTheWireTests : IAsyncLifetime
{
    private const string Host = "lender.example";

    private static readonly LendingKey Key = LendingKey.FromText("a household key long enough to be a key 0123");

    private static readonly byte[] Ecm = Encoding.ASCII.GetBytes("synthetic ecm over the wire");

    private readonly CardsOnHand cards = new();

    private CardLendingServer server = null!;

    public async Task InitializeAsync()
    {
        LendingPlan plan = new(new IPEndPoint(IPAddress.Loopback, 0), [Host], HopLimit: 2, RoundTrip: null, Leases: 2, Key);
        server = new CardLendingServer(plan, cards.Open, TimeProvider.System, logger: null);
        await server.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await server.StopAsync(CancellationToken.None);
        server.Dispose();
    }

    private CardLoan Loan => new(server.Bound!, Host, 2, Key);

    [Fact]
    public void AStreamIsUnscrambledWithKeysBorrowedOverTheWire()
    {
        SyntheticScrambledStream stream = new SyntheticScrambledStream()
            .Pat((1, 0x0101))
            .Scrambled(0x0111, Ecm, odd: false, seed: 1)
            .Pmt(0x0101, 1, 0x0901, [(0x0111, null), (0x0112, null)])
            .Ecm(0x0901, Ecm)
            .Scrambled(0x0111, Ecm, odd: true, seed: 2)
            .Scrambled(0x0112, Ecm, odd: false, seed: 3);

        using TransportStreamDescrambler descrambler = new(BorrowedCard.Open(Loan));

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
    }

    [Fact]
    public async Task ABorrowerThatGoesAwayGivesItsLeaseBack()
    {
        using (BorrowedCard card = BorrowedCard.Open(Loan))
        {
            card.Answer(Ecm);
            Assert.Equal(1, server.Lender.LeasesOut);
        }

        DateTimeOffset giveUp = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (server.Lender.LeasesOut > 0 && DateTimeOffset.UtcNow < giveUp)
        {
            await Task.Delay(20);
        }

        Assert.Equal(0, server.Lender.LeasesOut);
        Assert.True(cards.Connections.Single().Disposed);
    }

    [Fact]
    public async Task ARequestNamingAnotherHostIsNotAnswered()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => Ask("elsewhere.example"));
    }

    [Theory]
    [InlineData("X-Forwarded-For", "192.0.2.10")]
    [InlineData("Forwarded", "for=192.0.2.10")]
    [InlineData("X-Real-IP", "192.0.2.10")]
    [InlineData("CF-Connecting-IP", "192.0.2.10")]
    [InlineData("Via", "1.1 proxy.example")]
    public async Task ARequestThatCameThroughAProxyIsNotAnswered(string header, string value)
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => Ask(Host, (header, value)));
    }

    [Fact]
    public async Task ARequestABrowserMadeIsNotAnswered()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => Ask(Host, ("Origin", "http://lender.example")));
    }

    [Fact]
    public async Task AKnownHostIsAnswered()
    {
        using HttpResponseMessage response = await Ask(Host);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LendingProtocol.NonceLength, (await response.Content.ReadAsByteArrayAsync()).Length);
    }

    [Fact]
    public async Task NothingButLendingIsServedOnThePort()
    {
        using HttpResponseMessage response = await Ask(Host, path: "/api/greeting");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void ABorrowerWithAnotherKeyCannotOpenTheCard()
    {
        CardLoan wrong = Loan with { Key = LendingKey.FromText("another household key that is not the same one") };

        Assert.Throws<DescramblingException>(() => BorrowedCard.Open(wrong));
        Assert.Empty(cards.Connections);
    }

    [Fact]
    public void ALenderThatIsNotThereIsTheCardNotAnswering()
    {
        using Socket unused = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        unused.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        CardLoan nowhere = Loan with { Lender = (IPEndPoint)unused.LocalEndPoint! };

        Assert.Throws<DescramblingException>(() => BorrowedCard.Open(nowhere));
    }

    private async Task<HttpResponseMessage> Ask(
        string host,
        (string Name, string Value)? extra = null,
        string path = LendingProtocol.ChallengePath
    )
    {
        using HttpClient client = new(new SocketsHttpHandler { UseProxy = false });
        using HttpRequestMessage request = new(HttpMethod.Post, $"http://{server.Bound}{path}");
        request.Headers.Host = host;
        if (extra is (string name, string value))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await client.SendAsync(request);
    }
}
