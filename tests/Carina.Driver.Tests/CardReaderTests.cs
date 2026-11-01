using System.Net;

using Carina.Contracts;
using Carina.Driver.CardLending;
using Carina.Driver.Configuration;
using Carina.Driver.Descrambling;
using Carina.Driver.Ipc;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Driver.Tests;

public sealed class CardReaderNameTests
{
    [Theory]
    [InlineData("Example Reader [CCID Interface] (0123456789AB) 00 00", "Example Reader")]
    [InlineData("Example Reader (0123456789AB) 01 00", "Example Reader")]
    [InlineData("Example Reader", "Example Reader")]
    [InlineData("Example Reader 2", "Example Reader 2")]
    [InlineData("Example  Dual  Reader 00 01", "Example Dual Reader")]
    public void TheNameLeavesOutTheSerialAndTheSlot(string listed, string shown)
    {
        Assert.Equal(shown, CardReaderNames.Shown(listed));
    }
}

public sealed class LocalCardReaderTests
{
    private const string Reader = "Synthetic Reader 0";

    private static readonly DateTimeOffset Start = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ACardThatAnsweredIsInsertedInTheReaderItAnsweredIn()
    {
        var clock = new ManualTimeProvider(Start);
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection _);
        Descramblers cards = Descramblers.Over(() => service, logger: null, clock);

        cards.Open(HeadHold.EveryProgramme)?.Dispose();
        clock.Advance(TimeSpan.FromMinutes(5));
        CardReaderDto reader = cards.Inspect();

        Assert.Equal(CardReaderUse.Local, reader.Use);
        Assert.Equal(CardState.Inserted, reader.State);
        Assert.Null(reader.Problem);
        CardReaderSlotDto slot = Assert.Single(reader.Readers);
        Assert.Equal("Synthetic Reader 0", slot.Name);
        Assert.True(slot.InUse);
        Assert.Equal(Start, reader.LastAnsweredAt);
        Assert.Equal(Start.AddMinutes(5), reader.CheckedAt);
    }

    [Fact]
    public void LookingAtTheReaderNeverSpeaksToTheCard()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        Descramblers cards = Descramblers.Over(() => service, logger: null);

        cards.Inspect();

        Assert.Empty(card.Commands);
        Assert.Empty(service.Connections);
    }

    [Fact]
    public void AnEcmTheCardAnswersMovesWhenItLastAnswered()
    {
        var clock = new ManualTimeProvider(Start);
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection _);
        Descramblers cards = Descramblers.Over(() => service, logger: null, clock);
        SyntheticScrambledStream stream = new SyntheticScrambledStream()
            .Pat((1, 0x0101))
            .Pmt(0x0101, 1, 0x0901, [(0x0111, null)])
            .Ecm(0x0901, [0x01, 0x02, 0x03])
            .Scrambled(0x0111, [0x01, 0x02, 0x03], odd: true, seed: 1);

        using IDescrambler? descrambler = cards.Open(HeadHold.EveryProgramme);
        clock.Advance(TimeSpan.FromMinutes(1));
        descrambler!.Descramble(stream.Input);

        Assert.Equal(Start.AddMinutes(1), cards.Inspect().LastAnsweredAt);
    }

    [Fact]
    public void AReaderWithoutACardIsEmpty()
    {
        FakeSmartCardService service = new();
        service.Refusing(Reader, SmartCardCodes.NoSmartCard);
        Descramblers cards = Descramblers.Over(() => service, logger: null);

        CardReaderDto reader = cards.Inspect();

        Assert.Equal(CardState.Empty, reader.State);
        Assert.Equal(CardState.Empty, Assert.Single(reader.Readers).State);
        Assert.False(reader.Readers[0].InUse);
    }

    [Fact]
    public void ACardInTheReaderThatWillNotAnswerIsUnresponsive()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        card.InitialReturnCode = 0x0001;
        Descramblers cards = Descramblers.Over(() => service, logger: null);

        Assert.Null(cards.Open(HeadHold.EveryProgramme));
        CardReaderDto reader = cards.Inspect();

        Assert.Equal(CardState.Unresponsive, reader.State);
        Assert.Equal(CardProblem.CardSilent, reader.Problem);
    }

    [Fact]
    public void ACardTheReaderCallsMuteIsUnresponsive()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection _);
        service.Slots[Reader] = CardSlot.Mute;
        Descramblers cards = Descramblers.Over(() => service, logger: null);

        CardReaderDto reader = cards.Inspect();

        Assert.Equal(CardState.Unresponsive, reader.State);
        Assert.Equal(CardProblem.CardSilent, reader.Problem);
    }

    [Fact]
    public void NoReaderAttachedIsSaidSo()
    {
        Descramblers cards = Descramblers.Over(() => new FakeSmartCardService(), logger: null);

        CardReaderDto reader = cards.Inspect();

        Assert.Equal(CardState.NoReader, reader.State);
        Assert.Empty(reader.Readers);
    }

    [Fact]
    public void ASmartCardServiceThatIsNotRunningIsSaidSo()
    {
        Descramblers cards = Descramblers.Over(
            () => throw new SmartCardException("synthetic refusal", SmartCardCodes.NoService),
            logger: null
        );

        CardReaderDto reader = cards.Inspect();

        Assert.Equal(CardState.Unresponsive, reader.State);
        Assert.Equal(CardProblem.ServiceDown, reader.Problem);
    }

    [Fact]
    public void ADvbDriverWithoutTheSmartCardLibraryIsUnresponsiveForThatReason()
    {
        CardReaderDto reader = CardReaders.Inspect(
            NoDescrambling.Instance,
            Backed(TunerBackend.Dvb),
            lender: null,
            TimeProvider.System
        );

        Assert.Equal(CardReaderUse.Local, reader.Use);
        Assert.Equal(CardState.Unresponsive, reader.State);
        Assert.Equal(CardProblem.LibraryMissing, reader.Problem);
    }

    [Theory]
    [InlineData(TunerBackend.Fake)]
    [InlineData(TunerBackend.Usb)]
    public void ADriverThatAsksNoCardSaysItDoesNot(TunerBackend backend)
    {
        CardReaderDto reader = CardReaders.Inspect(NoDescrambling.Instance, Backed(backend), lender: null, TimeProvider.System);

        Assert.Equal(CardReaderUse.None, reader.Use);
        Assert.Equal(CardState.NotUsed, reader.State);
    }

    internal static DriverConfiguration Backed(TunerBackend backend) =>
        new(
            "/run/carina/driver.sock",
            [new OutputRootSettings("primary", "/srv/recordings")],
            6,
            new TunerSettings(backend),
            []
        );
}

public sealed class BorrowedCardReaderTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ABorrowedCardNamesTheLenderAndNotWhereItIs()
    {
        BorrowedCards cards = BorrowedCards.Over(() => new CardsOnHand().Open(), logger: null, "lender.example");

        CardReaderDto reader = cards.Inspect();

        Assert.Equal(CardReaderUse.Borrowed, reader.Use);
        Assert.Equal(CardState.Borrowed, reader.State);
        Assert.Equal("lender.example", reader.LenderHostName);
        Assert.Empty(reader.Readers);
    }

    [Fact]
    public void ALenderThatCannotBeReachedIsSaidSo()
    {
        BorrowedCards cards = BorrowedCards.Over(
            () => throw new DescramblingException("synthetic", DescramblingFailure.LenderUnreachable),
            logger: null,
            "lender.example"
        );

        Assert.Null(cards.Open(HeadHold.EveryProgramme));

        Assert.Equal(CardProblem.LenderUnreachable, cards.Inspect().Problem);
    }

    [Fact]
    public void ALenderThatRefusedIsSaidSo()
    {
        BorrowedCards cards = BorrowedCards.Over(
            () => throw new DescramblingException("synthetic", DescramblingFailure.LenderRefused),
            logger: null,
            "lender.example"
        );

        cards.Open(HeadHold.EveryProgramme);

        Assert.Equal(CardProblem.LenderRefused, cards.Inspect().Problem);
    }

    [Fact]
    public void ALoanThatWorkedAfterAFailureClearsTheProblem()
    {
        var clock = new ManualTimeProvider(Start);
        bool reachable = false;
        BorrowedCards cards = BorrowedCards.Over(
            () => reachable
                ? new CardsOnHand().Open()
                : throw new DescramblingException("synthetic", DescramblingFailure.LenderUnreachable),
            logger: null,
            "lender.example",
            clock
        );

        cards.Open(HeadHold.EveryProgramme);
        reachable = true;
        clock.Advance(TimeSpan.FromSeconds(5));
        cards.Open(HeadHold.EveryProgramme)?.Dispose();

        CardReaderDto reader = cards.Inspect();
        Assert.Null(reader.Problem);
        Assert.Equal(Start.AddSeconds(5), reader.LastAnsweredAt);
    }
}

public sealed class CardReaderApiTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static CancellationToken Soon() => new CancellationTokenSource(Patience).Token;

    [Fact]
    public async Task TheReaderIsAnsweredWithoutTheCardsId()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection _);
        Descramblers cards = Descramblers.Over(() => service, logger: null);
        cards.Open(HeadHold.EveryProgramme)?.Dispose();

        await using DriverUnderTest driver = await DriverUnderTest.Start(
            reshapeServices: services => services.AddSingleton<IDescramblerFactory>(cards)
        );
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.CardReader, Soon());
        string body = await response.Content.ReadAsStringAsync(Soon());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("\"state\":\"inserted\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("0A0B0C0D0E0F", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cardId", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ASyntheticDriverSaysItAsksNoCard()
    {
        await using DriverUnderTest driver = await DriverUnderTest.Start();
        using HttpClient client = driver.Client();

        using HttpResponseMessage response = await client.GetAsync(DriverEndpoints.CardReader, Soon());
        CardReaderDto? reader = await DriverUnderTest.Read(response, DriverJson.Context.CardReaderDto);

        Assert.Equal(CardState.NotUsed, reader?.State);
        Assert.Null(reader?.LeasesOut);
        Assert.NotNull(reader?.CheckedAt);
    }

    [Fact]
    public void TheDriverSaysItAnswersForItsReader()
    {
        Assert.Contains(DriverCapabilities.CardReaderStatus, DriverGreeting.Capabilities);
    }
}

public sealed class ReaderSlotTests
{
    [Theory]
    [InlineData(0x0010u, CardSlot.Empty)]
    [InlineData(0x0022u, CardSlot.Present)]
    [InlineData(0x0122u, CardSlot.Present)]
    [InlineData(0x0222u, CardSlot.Mute)]
    [InlineData(0x0008u, CardSlot.Unknown)]
    public void TheSlotIsReadFromTheEventStateTheServiceAnswersWith(uint eventState, CardSlot slot)
    {
        Assert.Equal(slot, PcscLibrary.SlotOf(eventState));
    }
}
