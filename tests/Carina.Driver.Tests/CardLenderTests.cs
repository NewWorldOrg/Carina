using System.Text;

using Carina.Driver.CardLending;
using Carina.Driver.Descrambling;

namespace Carina.Driver.Tests;

public sealed class CardLenderTests
{
    private const string Host = "lender.example";

    private const string Connection = "connection-1";

    private static readonly LendingKey Key = LendingKey.FromText("a household key long enough to be a key 0123");

    private static readonly LendingKey AnotherKey = LendingKey.FromText("another household key that is not the same one");

    private static readonly byte[] Ecm = Encoding.ASCII.GetBytes("synthetic ecm for lending");

    private readonly ManualTimeProvider time = new(new DateTimeOffset(2026, 10, 30, 21, 0, 0, TimeSpan.Zero));

    private readonly CardsOnHand cards = new();

    private CardLender Lender(int leases = 2, TimeSpan? roundTrip = null) =>
        new(Key, cards.Open, new CardLendingLimits(leases, roundTrip), time);

    private static LendingSession Leased(CardLender lender, string connection = Connection)
    {
        BorrowerHandshake handshake = BorrowerHandshake.Begin(Key, Host, lender.Challenge(connection));
        LendingReply reply = lender.Lease(connection, Host, handshake.Request);

        Assert.Equal(LendingReply.Ok, reply.Status);

        return handshake.Accept(reply.Body);
    }

    private static byte[] MaterialFor(byte[] ecm) =>
        [.. SyntheticCardKeys.SystemKey, .. SyntheticCardKeys.InitialValue, .. SyntheticCardKeys.PairFor(ecm)];

    [Fact]
    public void ABorrowerHoldingTheKeyGetsTheKeysTheCardAnswered()
    {
        using CardLender lender = Lender();
        LendingSession session = Leased(lender);

        EcmAnswer answer = session.Open(lender.Answer(Connection, Ecm).Body);

        Assert.True(answer.Granted);
        Assert.Equal(0x0800, answer.ReturnCode);
        Assert.Equal(MaterialFor(Ecm), answer.KeyMaterial.ToArray());
        Assert.Equal(SyntheticCardKeys.CaSystemId, session.CaSystemId);
    }

    [Fact]
    public void TheKeysTravelSealed()
    {
        using CardLender lender = Lender();
        Leased(lender);

        byte[] body = lender.Answer(Connection, Ecm).Body;

        Assert.False(Contains(body, SyntheticCardKeys.SystemKey));
        Assert.False(Contains(body, SyntheticCardKeys.PairFor(Ecm)));
    }

    [Fact]
    public void ABorrowerWithAnotherKeyIsRefusedBeforeTheCardIsOpened()
    {
        using CardLender lender = Lender();
        BorrowerHandshake handshake = BorrowerHandshake.Begin(AnotherKey, Host, lender.Challenge(Connection));

        LendingReply reply = lender.Lease(Connection, Host, handshake.Request);

        Assert.Equal(LendingReply.Forbidden, reply.Status);
        Assert.Empty(cards.Connections);
    }

    [Fact]
    public void AProofMadeForAnotherHostIsRefused()
    {
        using CardLender lender = Lender();
        BorrowerHandshake handshake = BorrowerHandshake.Begin(Key, "elsewhere.example", lender.Challenge(Connection));

        Assert.Equal(LendingReply.Forbidden, lender.Lease(Connection, Host, handshake.Request).Status);
    }

    [Fact]
    public void TheHostIsComparedWithoutRegardToCase()
    {
        using CardLender lender = Lender();
        BorrowerHandshake handshake = BorrowerHandshake.Begin(Key, "Lender.Example", lender.Challenge(Connection));

        Assert.Equal(LendingReply.Ok, lender.Lease(Connection, Host, handshake.Request).Status);
    }

    [Fact]
    public void AChallengeIsAnsweredOnce()
    {
        using CardLender lender = Lender();
        BorrowerHandshake handshake = BorrowerHandshake.Begin(Key, Host, lender.Challenge(Connection));
        lender.Lease(Connection, Host, handshake.Request);

        Assert.Equal(LendingReply.Forbidden, lender.Lease(Connection, Host, handshake.Request).Status);
    }

    [Fact]
    public void AChallengeIssuedOnAnotherConnectionDoesNotCount()
    {
        using CardLender lender = Lender();
        BorrowerHandshake handshake = BorrowerHandshake.Begin(Key, Host, lender.Challenge(Connection));

        Assert.Equal(LendingReply.Forbidden, lender.Lease("connection-2", Host, handshake.Request).Status);
    }

    [Fact]
    public void AChallengeLeftTooLongIsNoLongerAnswered()
    {
        using CardLender lender = Lender();
        BorrowerHandshake handshake = BorrowerHandshake.Begin(Key, Host, lender.Challenge(Connection));
        time.Advance(CardLender.ChallengeLife + TimeSpan.FromMilliseconds(1));

        Assert.Equal(LendingReply.Forbidden, lender.Lease(Connection, Host, handshake.Request).Status);
    }

    [Fact]
    public void ABorrowerSlowerThanTheRoundTripAllowedIsRefused()
    {
        using CardLender lender = Lender(roundTrip: TimeSpan.FromMilliseconds(5));
        BorrowerHandshake handshake = BorrowerHandshake.Begin(Key, Host, lender.Challenge(Connection));
        time.Advance(TimeSpan.FromMilliseconds(6));

        LendingReply reply = lender.Lease(Connection, Host, handshake.Request);

        Assert.Equal(LendingReply.Forbidden, reply.Status);
        Assert.Equal("round-trip", Encoding.ASCII.GetString(reply.Body));
        Assert.Empty(cards.Connections);
    }

    [Fact]
    public void ABorrowerWithinTheRoundTripAllowedIsLent()
    {
        using CardLender lender = Lender(roundTrip: TimeSpan.FromMilliseconds(5));
        BorrowerHandshake handshake = BorrowerHandshake.Begin(Key, Host, lender.Challenge(Connection));
        time.Advance(TimeSpan.FromMilliseconds(4));

        Assert.Equal(LendingReply.Ok, lender.Lease(Connection, Host, handshake.Request).Status);
    }

    [Fact]
    public void AnEcmOnAConnectionWithNoLeaseIsToldToLeaseFirst()
    {
        using CardLender lender = Lender();

        Assert.Equal(LendingReply.NoLease, lender.Answer(Connection, Ecm).Status);
        Assert.Empty(cards.Connections);
    }

    [Fact]
    public void NoMoreLeasesThanAllowedAreOut()
    {
        using CardLender lender = Lender(leases: 1);
        Leased(lender, "connection-1");
        BorrowerHandshake second = BorrowerHandshake.Begin(Key, Host, lender.Challenge("connection-2"));

        Assert.Equal(LendingReply.Busy, lender.Lease("connection-2", Host, second.Request).Status);

        lender.Release("connection-1");
        Leased(lender, "connection-2");
        Assert.Equal(1, lender.LeasesOut);
    }

    [Fact]
    public void ReleasingALeaseLetsGoOfItsCard()
    {
        using CardLender lender = Lender();
        Leased(lender);

        lender.Release(Connection);

        Assert.Equal(0, lender.LeasesOut);
        Assert.True(cards.Connections.Single().Disposed);
        Assert.Equal(LendingReply.NoLease, lender.Answer(Connection, Ecm).Status);
    }

    [Fact]
    public void LeasingAgainOnOneConnectionLetsGoOfTheCardItHeld()
    {
        using CardLender lender = Lender(leases: 1);
        Leased(lender);

        Leased(lender);

        Assert.Equal(1, lender.LeasesOut);
    }

    [Fact]
    public void ACardThatWillNotUnlockAnEcmIsPassedOnWithoutKeys()
    {
        FakeSmartCardService refusing = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        card.EcmReturnCode = _ => 0x8901;
        using CardLender lender = new(Key, () => ConditionalAccessCard.Open(refusing), new CardLendingLimits(2, null), time);
        LendingSession session = Leased(lender);

        EcmAnswer answer = session.Open(lender.Answer(Connection, Ecm).Body);

        Assert.False(answer.Granted);
        Assert.Equal(0x8901, answer.ReturnCode);
        Assert.True(answer.KeyMaterial.IsEmpty);
    }

    [Fact]
    public void AnAnswerSealedForAnotherLeaseCannotBeOpened()
    {
        using CardLender lender = Lender();
        LendingSession first = Leased(lender, "connection-1");
        Leased(lender, "connection-2");

        byte[] forTheSecond = lender.Answer("connection-2", Ecm).Body;

        Assert.Throws<DescramblingException>(() => first.Open(forTheSecond));
    }

    [Fact]
    public void AnAnswerHandedBackASecondTimeIsNotTaken()
    {
        using CardLender lender = Lender();
        LendingSession session = Leased(lender);
        byte[] body = lender.Answer(Connection, Ecm).Body;
        session.Open(body);

        Assert.Throws<DescramblingException>(() => session.Open(body));
    }

    [Fact]
    public void ALenderThatCannotProveTheKeyIsNotBelieved()
    {
        using CardLender lender = Lender();
        BorrowerHandshake handshake = BorrowerHandshake.Begin(Key, Host, lender.Challenge(Connection));
        byte[] forged = new byte[LendingProtocol.LeaseReplyLength];

        Assert.Throws<DescramblingException>(() => handshake.Accept(forged));
    }

    [Fact]
    public void MoreEcmsInASecondThanACardShouldBeAskedAreTurnedAway()
    {
        using CardLender lender = Lender();
        Leased(lender);

        for (int asked = 0; asked < CardLender.MostEcmsPerSecond; asked++)
        {
            Assert.Equal(LendingReply.Ok, lender.Answer(Connection, Ecm).Status);
        }

        Assert.Equal(LendingReply.Busy, lender.Answer(Connection, Ecm).Status);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(LendingReply.Ok, lender.Answer(Connection, Ecm).Status);
    }

    [Fact]
    public void ACardThatStopsAnsweringEndsTheLease()
    {
        FakeSmartCardService failing = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        using CardLender lender = new(Key, () => ConditionalAccessCard.Open(failing), new CardLendingLimits(2, null), time);
        Leased(lender);
        card.Failures.Enqueue(SmartCardCodes.NoService);

        Assert.Equal(LendingReply.NoCard, lender.Answer(Connection, Ecm).Status);
        Assert.Equal(0, lender.LeasesOut);
        Assert.True(card.Disposed);
        Assert.Equal(LendingReply.NoLease, lender.Answer(Connection, Ecm).Status);
    }

    [Fact]
    public void ALenderWithNoCardSaysSo()
    {
        using CardLender lender = new(
            Key,
            () => throw new DescramblingException("No smart-card reader is attached."),
            new CardLendingLimits(2, null),
            time
        );
        BorrowerHandshake handshake = BorrowerHandshake.Begin(Key, Host, lender.Challenge(Connection));

        Assert.Equal(LendingReply.NoCard, lender.Lease(Connection, Host, handshake.Request).Status);
        Assert.Equal(0, lender.LeasesOut);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(ConditionalAccessCard.LongestEcmBody + 1)]
    public void AnEcmOutsideTheLengthsACardTakesIsRefused(int length)
    {
        using CardLender lender = Lender();
        Leased(lender);

        Assert.Equal(LendingReply.Malformed, lender.Answer(Connection, new byte[length]).Status);
    }

    [Fact]
    public void ALeaseRequestOfTheWrongLengthIsRefused()
    {
        using CardLender lender = Lender();
        lender.Challenge(Connection);

        Assert.Equal(LendingReply.Malformed, lender.Lease(Connection, Host, new byte[3]).Status);
    }

    [Fact]
    public void StoppingTheLenderLetsGoOfEveryCard()
    {
        CardLender lender = Lender();
        Leased(lender, "connection-1");
        Leased(lender, "connection-2");

        lender.Dispose();

        Assert.All(cards.Connections, connection => Assert.True(connection.Disposed));
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;
}

public sealed class LendingKeyTests
{
    [Fact]
    public void AKeyShorterThanTheLeastIsRefused()
    {
        Assert.Throws<ArgumentException>(() => LendingKey.FromText(new string('k', LendingKey.ShortestText - 1)));
    }

    [Fact]
    public void TheSpaceAroundTheKeyIsNotPartOfIt()
    {
        string text = new('k', LendingKey.ShortestText);
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection _);
        using CardLender lender = new(
            LendingKey.FromText($"  {text}\n"),
            () => ConditionalAccessCard.Open(service),
            new CardLendingLimits(1, null),
            TimeProvider.System
        );
        BorrowerHandshake handshake = BorrowerHandshake.Begin(LendingKey.FromText(text), "h", lender.Challenge("c"));

        Assert.Equal(LendingReply.Ok, lender.Lease("c", "h", handshake.Request).Status);
    }

    [Fact]
    public void TheKeyDoesNotSayWhatItIs()
    {
        string text = "a household key long enough to be a key 0123";

        Assert.DoesNotContain(text, LendingKey.FromText(text).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AKeyFileOthersCanReadIsRefused()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("carina-lending-").FullName, "key");

        try
        {
            File.WriteAllText(path, new string('k', LendingKey.ShortestText));
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);

            Assert.Null(LendingKey.ReadFile(path, out string? problem));
            Assert.Contains("others", problem, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void AKeyFileOnlyItsOwnerCanReadIsRead()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("carina-lending-").FullName, "key");

        try
        {
            File.WriteAllText(path, new string('k', LendingKey.ShortestText) + "\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            Assert.NotNull(LendingKey.ReadFile(path, out string? problem));
            Assert.Null(problem);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void AMissingKeyFileIsAProblemAndNotAnException()
    {
        Assert.Null(LendingKey.ReadFile("/nonexistent/carina/key", out string? problem));
        Assert.NotNull(problem);
    }
}
