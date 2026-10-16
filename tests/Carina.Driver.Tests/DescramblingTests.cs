using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Descrambling;
using Carina.Driver.Ipc;
using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class DescramblingTests
{
    [Fact]
    public void WhatTheCardUnlockedIsWhatTheSessionReads()
    {
        var source = new ChunkByChunkTunerDevice([[1, 2, 3]]);
        var card = new ScriptedDescrambler(scrambled => [.. scrambled.ToArray().Select(b => (byte)(b + 10))]);

        using var device = new DescramblingTunerDevice(source, card);

        Assert.Equal([11, 12, 13], device.Read(3, CancellationToken.None));
    }

    [Fact]
    public void TheTunerIsReadAgainWhileTheDescramblerIsStillHoldingEverythingItWasGiven()
    {
        var source = new ChunkByChunkTunerDevice([[1], [2], [3]]);
        var card = new ScriptedDescrambler(scrambled =>
            scrambled.Length > 0 && scrambled[0] is 3 ? [30] : []);

        using var device = new DescramblingTunerDevice(source, card);

        Assert.Equal([30], device.Read(1, CancellationToken.None));
        Assert.Equal(3, source.Reads);
    }

    [Fact]
    public void AnEmptyReadIsHandedOnSoTheSessionStillCallsItTheEndOfTheStream()
    {
        var source = new ChunkByChunkTunerDevice([[]]);
        var card = new ScriptedDescrambler(_ => [1]);

        using var device = new DescramblingTunerDevice(source, card);

        Assert.Empty(device.Read(1, CancellationToken.None));
    }

    [Fact]
    public void ClosingTheSessionLetsGoOfBothTheCardAndTheTuner()
    {
        var source = new ChunkByChunkTunerDevice([]);
        var card = new ScriptedDescrambler(_ => []);

        new DescramblingTunerDevice(source, card).Dispose();

        Assert.True(source.Disposed);
        Assert.True(card.Disposed);
    }

    [Fact]
    public void TheTunersOwnReadingsAreStillTheOnesReported()
    {
        var source = new ChunkByChunkTunerDevice([]) { Overflows = 42 };
        var card = new ScriptedDescrambler(_ => []);

        using var device = new DescramblingTunerDevice(source, card);

        Assert.Equal(42, device.Overflows);
    }

    [Fact]
    public void ACardThatDiesPartWayThroughLeavesTheRecordingRunningOnScrambledBytes()
    {
        int reads = 0;
        var source = new ChunkByChunkTunerDevice([[1], [2], [3]]);
        var card = new ScriptedDescrambler(scrambled =>
            ++reads is 1
                ? [.. scrambled.ToArray()]
                : throw new DescramblingException("the card went away"));

        using var device = new DescramblingTunerDevice(source, card);

        Assert.Equal([1], device.Read(1, CancellationToken.None));
        Assert.Equal([2], device.Read(1, CancellationToken.None));
        Assert.Equal([3], device.Read(1, CancellationToken.None));
        Assert.True(card.Disposed);
    }

    [Fact]
    public void WhatTheCardHadTakenButNeverReadIsHandedOnRatherThanDroppedWithIt()
    {
        var source = new ChunkByChunkTunerDevice([[3]]);
        var card = new ScriptedDescrambler(
            _ => throw new DescramblingException("the card went away"))
        {
            Unread = [1, 2],
        };

        using var device = new DescramblingTunerDevice(source, card);

        Assert.Equal([1, 2, 3], device.Read(1, CancellationToken.None));
    }

    [Fact]
    public void ACardThatCannotEvenSayWhatItHeldStillLetsTheStreamThrough()
    {
        var source = new ChunkByChunkTunerDevice([[3]]);
        var card = new ThrowingDescrambler();

        using var device = new DescramblingTunerDevice(source, card);

        Assert.Equal([3], device.Read(1, CancellationToken.None));
    }

    [Fact]
    public void ADriverWithNoCardOffersNothingAndHandsOutNothing()
    {
        Assert.False(NoDescrambling.Instance.Unscrambles);
        Assert.Null(NoDescrambling.Instance.Open(HeadHold.EveryProgramme));
    }

    [Fact]
    public void ACardThatAnswersAgainUnscramblesTheRestOfTheStreamWithNothingRestarted()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var source = new ChunkByChunkTunerDevice([[1], [2]]);
        var dying = new ScriptedDescrambler(
            _ => throw new DescramblingException("the card went away"));
        var cards = new CardsOnRequest(() =>
            new ScriptedDescrambler(scrambled =>
                [.. scrambled.ToArray().Select(b => (byte)(b + 10))]));

        using var device = new DescramblingTunerDevice(source, dying, cards, clock);

        Assert.Equal([1], device.Read(1, CancellationToken.None));

        clock.Advance(DescramblingTunerDevice.BetweenAsksForTheCard);

        Assert.Equal([12], device.Read(1, CancellationToken.None));
        Assert.True(dying.Disposed);
    }

    [Fact]
    public void ATunerOpenedWhileTheCardIsAwayIsReadAsItComesAndTakesTheCardWhenItAnswers()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var source = new ChunkByChunkTunerDevice([[1], [2]]);
        bool answering = false;
        var cards = new CardsOnRequest(() =>
            answering
                ? new ScriptedDescrambler(scrambled =>
                    [.. scrambled.ToArray().Select(b => (byte)(b + 10))])
                : null);

        using var device = new DescramblingTunerDevice(source, descrambler: null, cards, clock);

        Assert.Equal([1], device.Read(1, CancellationToken.None));

        answering = true;
        clock.Advance(DescramblingTunerDevice.BetweenAsksForTheCard);

        Assert.Equal([12], device.Read(1, CancellationToken.None));
    }

    [Fact]
    public void ACardAskedForAgainIsAskedToHoldTheHeadAsTheTunerWasOpenedTo()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var source = new ChunkByChunkTunerDevice([[1]]);
        var cards = new CardsOnRequest(() => null);

        using var device = new DescramblingTunerDevice(
            source,
            descrambler: null,
            cards,
            clock,
            hold: HeadHold.ForProgramme(1024));

        clock.Advance(DescramblingTunerDevice.BetweenAsksForTheCard);
        device.Read(1, CancellationToken.None);

        Assert.Equal([HeadHold.ForProgramme(1024)], cards.Holds);
    }

    [Fact]
    public void TheCardIsNotAskedForOnEveryPacketWhileItIsAway()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var source = new ChunkByChunkTunerDevice([[1], [2], [3], [4]]);
        var cards = new CardsOnRequest(() => null);

        using var device = new DescramblingTunerDevice(source, descrambler: null, cards, clock);

        device.Read(1, CancellationToken.None);
        device.Read(1, CancellationToken.None);
        device.Read(1, CancellationToken.None);

        Assert.Equal(0, cards.Asked);

        clock.Advance(DescramblingTunerDevice.BetweenAsksForTheCard);
        device.Read(1, CancellationToken.None);

        Assert.Equal(1, cards.Asked);
    }

    [Fact]
    public void ATunerIsOnlyReadThroughACardWhenThereIsOneOrSomewhereToAskForOne()
    {
        var source = new ChunkByChunkTunerDevice([]);

        Assert.Throws<ArgumentException>(() =>
            new DescramblingTunerDevice(source, descrambler: null));
    }

    [Fact]
    public void ADriverThatCannotUnscrambleDoesNotSayItCan()
    {
        Assert.DoesNotContain(
            DriverCapabilities.Descrambling,
            DriverGreeting.Unscrambling(descrambling: false));

        Assert.DoesNotContain(
            DriverCapabilities.Descrambling,
            DriverGreeting.ForThisProcess(descrambling: false).Capabilities);
    }

    [Fact]
    public void ADriverThatCanUnscrambleSaysSoInItsGreeting()
    {
        Assert.Contains(
            DriverCapabilities.Descrambling,
            DriverGreeting.Unscrambling(descrambling: true));

        Assert.Contains(
            DriverCapabilities.Descrambling,
            DriverGreeting.ForThisProcess(descrambling: true).Capabilities);
    }

    [Fact]
    public void SayingTheDriverUnscramblesAddsThatAndNothingElse()
    {
        Assert.Equal(
            [.. DriverGreeting.Unscrambling(descrambling: false), DriverCapabilities.Descrambling],
            DriverGreeting.Unscrambling(descrambling: true));
    }

    [Fact]
    public void ASyntheticTunerIsNeverUnscrambledHoweverManyCardsTheMachineHas()
    {
        Assert.Same(
            NoDescrambling.Instance,
            Descramblers.For(Backed(TunerBackend.Fake), logger: null));
    }

    [Fact]
    public void ADriverThatWasGivenNoTunerBackendAsksNoCardForAnything()
    {
        Assert.Same(
            NoDescrambling.Instance,
            Descramblers.For(Backed(TunerBackend.Unspecified), logger: null));
    }

    private static DriverConfiguration Backed(TunerBackend backend) =>
        new(null, null, 0, new TunerSettings(backend), null);
}

public sealed class EndOfTheScriptException() : IOException("The script ran out of chunks.");

public sealed class ChunkByChunkTunerDevice(IReadOnlyList<byte[]> chunks) : ITunerDevice
{
    private int next;

    public long Overflows { get; set; }

    public int Reads => next;

    public bool Disposed { get; private set; }

    public byte[] Read(int count, CancellationToken cancellationToken) =>
        next < chunks.Count ? chunks[next++] : throw new EndOfTheScriptException();

    public void Dispose() => Disposed = true;
}

public delegate byte[] Unlocking(ReadOnlySpan<byte> stream);

public sealed class ThrowingDescrambler : IDescrambler
{
    public byte[] Descramble(ReadOnlySpan<byte> stream) =>
        throw new DescramblingException("the card went away");

    public byte[] WhatItCouldNotRead() =>
        throw new DescramblingException("and cannot say what it had taken");

    public void Dispose() { }
}

public sealed class CardsOnRequest(Func<IDescrambler?> answer) : IDescramblerFactory
{
    public List<HeadHold> Holds { get; } = [];

    public int Asked => Holds.Count;

    public bool Unscrambles => true;

    public CardIdentity? CardInHand => null;

    public IDescrambler? Open(HeadHold hold)
    {
        Holds.Add(hold);

        return answer();
    }
}

public sealed class ScriptedDescrambler(Unlocking unlock) : IDescrambler
{
    public byte[] Unread { get; set; } = [];

    public bool Disposed { get; private set; }

    public byte[] Descramble(ReadOnlySpan<byte> stream) => unlock(stream);

    public byte[] WhatItCouldNotRead() => Unread;

    public void Dispose() => Disposed = true;
}

public sealed class DescramblingWiringTests
{
    private const FrontendStatus Locked =
        FrontendStatus.Signal
        | FrontendStatus.Carrier
        | FrontendStatus.Viterbi
        | FrontendStatus.Sync
        | FrontendStatus.Lock;

    [Fact]
    public void ARealTunerIsHandedToTheCardBeforeAnythingElseSeesItsBytes()
    {
        ITunerDevice opened = Open(new OneDescramblerFactory());

        Assert.IsType<DescramblingTunerDevice>(opened);

        opened.Dispose();
    }

    [Fact]
    public void ARealTunerOnAMachineWithNoCardIsReadExactlyAsItWasBefore()
    {
        ITunerDevice opened = Open(NoDescrambling.Instance);

        Assert.IsNotType<DescramblingTunerDevice>(opened);

        opened.Dispose();
    }

    [Fact]
    public void ATunerOpenedWhileNoCardAnswersIsStillTheOneThatWillAskAgain()
    {
        ITunerDevice opened = Open(new OneDescramblerFactory { Answers = false });

        Assert.IsType<DescramblingTunerDevice>(opened);

        opened.Dispose();
    }

    [Fact]
    public void TheCardIsAskedToHoldTheHeadAsTheTunerIsOpenedTo()
    {
        var descramblers = new OneDescramblerFactory();

        using ITunerDevice opened = Open(descramblers, HeadHold.ForProgramme(1024));

        Assert.Equal([HeadHold.ForProgramme(1024)], descramblers.Holds);
    }

    [Fact]
    public void ATunerOpenedWhileNoCardAnswersAsksAgainWithTheSameHold()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var calls = new ScriptedDvbSystemCalls(clock);
        var descramblers = new OneDescramblerFactory { Answers = false };
        calls.Deliver([0x47]);

        using ITunerDevice opened = Open(descramblers, HeadHold.ForProgramme(1024), calls, clock);
        clock.Advance(DescramblingTunerDevice.BetweenAsksForTheCard);
        opened.Read(188, CancellationToken.None);

        Assert.Equal([HeadHold.ForProgramme(1024), HeadHold.ForProgramme(1024)], descramblers.Holds);
    }

    private static ITunerDevice Open(
        IDescramblerFactory descramblers,
        HeadHold? hold = null,
        ScriptedDvbSystemCalls? calls = null,
        ManualTimeProvider? clock = null)
    {
        ManualTimeProvider time = clock ?? new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        ScriptedDvbSystemCalls system = calls ?? new ScriptedDvbSystemCalls(time);
        system.ReportStatus(Locked);

        TunerDeviceFactory factory = TunerDeviceFactory.Using(
            new DriverConfiguration(null, null, 0, new TunerSettings(TunerBackend.Dvb), null),
            time,
            system,
            descramblers);

        return factory.Create(
            new DeviceSettings("pt3-0", DeviceKind.Terrestrial, "/dev/dvb/adapter0/frontend0"),
            new TuningRequest(TunerKind.Terrestrial, 27),
            tune: null,
            hold ?? HeadHold.EveryProgramme);
    }

    private sealed class OneDescramblerFactory : IDescramblerFactory
    {
        public bool Answers { get; set; } = true;

        public List<HeadHold> Holds { get; } = [];

        public bool Unscrambles => true;

        public CardIdentity? CardInHand => null;

        public IDescrambler? Open(HeadHold hold)
        {
            Holds.Add(hold);

            return Answers ? new ScriptedDescrambler(_ => [1]) : null;
        }
    }
}

public sealed class DescramblersTests
{
    [Fact]
    public void NoCardIsInHandUntilOneHasAnswered()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection _);

        Descramblers descramblers = Descramblers.Over(() => service, logger: null);

        Assert.Null(descramblers.CardInHand);
    }

    [Fact]
    public void TheCardThatAnsweredAsATunerOpenedIsTheOneInHand()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection _);
        Descramblers descramblers = Descramblers.Over(() => service, logger: null);

        using IDescrambler? descrambler = descramblers.Open(HeadHold.EveryProgramme);

        Assert.NotNull(descrambler);
        Assert.Equal(SyntheticCardKeys.CaSystemId, descramblers.CardInHand!.CaSystemId);
        Assert.Equal("0A0B0C0D0E0F", descramblers.CardInHand.CardId);
    }

    [Fact]
    public void TheCardLastHeardOfStaysInHandWhenTheReaderLaterHasNone()
    {
        FakeSmartCardService answering = FakeSmartCardService.WithOneCard(out FakeCardConnection _);
        FakeSmartCardService empty = new();
        Queue<FakeSmartCardService> services = new([answering, empty]);
        Descramblers descramblers = Descramblers.Over(services.Dequeue, logger: null);

        using IDescrambler? first = descramblers.Open(HeadHold.EveryProgramme);
        using IDescrambler? second = descramblers.Open(HeadHold.EveryProgramme);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal("0A0B0C0D0E0F", descramblers.CardInHand!.CardId);
    }

    [Fact]
    public void ACardOpenedForALiveViewerHandsOnTheHeadOnceThatViewersProgrammeCanBeRead()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection _);
        Descramblers descramblers = Descramblers.Over(() => service, logger: null);
        SyntheticScrambledStream head = new SyntheticScrambledStream()
            .Pat((1, 0x0101), (2, 0x0102))
            .Pmt(0x0101, 1, 0x0901, [(0x0111, null)])
            .Ecm(0x0901, [0x01, 0x02, 0x03])
            .Scrambled(0x0111, [0x01, 0x02, 0x03], odd: true, seed: 1);

        using IDescrambler? descrambler = descramblers.Open(HeadHold.ForProgramme(1));

        Assert.Equal(head.Output, descrambler!.Descramble(head.Input));
    }

    [Fact]
    public void ADriverThatAsksNoCardHasNoneInHand()
    {
        Assert.Null(NoDescrambling.Instance.CardInHand);
    }
}

public sealed class DescramblingOnThisMachineTests
{
    [Fact]
    public void TheCardIsAskedForOnlyWhatItTakesToKnowThatItIsThere()
    {
        IDescramblerFactory descramblers = Descramblers.Probe(logger: null);

        using IDescrambler? descrambler = descramblers.Open(HeadHold.EveryProgramme);

        if (descrambler is null)
        {
            Assert.Null(NoDescrambling.Instance.Open(HeadHold.EveryProgramme));

            return;
        }

        Assert.True(descramblers.Unscrambles);
        Assert.Empty(descrambler.Descramble([]));
    }
}
