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
        Assert.False(NoDescrambling.Instance.CardAnswered);
        Assert.Null(NoDescrambling.Instance.Open());
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
    public void ACardThatStoppedAnsweringLeavesTheTunerReadableRatherThanRefusingToTune()
    {
        ITunerDevice opened = Open(new OneDescramblerFactory { Answers = false });

        Assert.IsNotType<DescramblingTunerDevice>(opened);

        opened.Dispose();
    }

    private static ITunerDevice Open(IDescramblerFactory descramblers)
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var calls = new ScriptedDvbSystemCalls(clock);
        calls.ReportStatus(Locked);

        TunerDeviceFactory factory = TunerDeviceFactory.Using(
            new DriverConfiguration(null, null, 0, new TunerSettings(TunerBackend.Dvb), null),
            clock,
            calls,
            descramblers);

        return factory.Create(
            new DeviceSettings("pt3-0", DeviceKind.Terrestrial, "/dev/dvb/adapter0/frontend0"),
            new TuningRequest(TunerKind.Terrestrial, 27),
            tune: null);
    }

    private sealed class OneDescramblerFactory : IDescramblerFactory
    {
        public bool Answers { get; set; } = true;

        public bool CardAnswered => true;

        public IDescrambler? Open() =>
            Answers ? new ScriptedDescrambler(_ => [1]) : null;
    }
}

public sealed class DescramblingOnThisMachineTests
{
    [Fact]
    public void TheCardIsAskedForOnlyWhatItTakesToKnowThatItIsThere()
    {
        IDescramblerFactory descramblers = Descramblers.Probe(logger: null);

        if (!descramblers.CardAnswered)
        {
            Assert.Same(NoDescrambling.Instance, descramblers);

            return;
        }

        using IDescrambler? descrambler = descramblers.Open();

        Assert.NotNull(descrambler);
        Assert.Empty(descrambler.Descramble([]));
    }
}
