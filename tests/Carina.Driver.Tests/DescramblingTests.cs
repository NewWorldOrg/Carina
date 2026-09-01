using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Descrambling;
using Carina.Driver.Ipc;
using Carina.Driver.Recording;
using Carina.Driver.Sessions;
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
    public void ASessionThatWouldWriteAnEmptyFileRatherThanAScrambledOneFailsInstead()
    {
        var source = new ChunkByChunkTunerDevice(
            Enumerable.Repeat(new byte[1024 * 1024], 16).ToArray());
        var card = new ScriptedDescrambler(_ => []);

        using var device = new DescramblingTunerDevice(source, card);

        DescramblingException refused = Assert.Throws<DescramblingException>(
            () => device.Read(1024 * 1024, CancellationToken.None));

        Assert.Contains("handed none of it back", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADescramblerThatHasAnsweredOnceIsNeverJudgedOnWhatItHoldsAfterwards()
    {
        int reads = 0;
        var source = new ChunkByChunkTunerDevice(
            Enumerable.Repeat(new byte[1024 * 1024], 32).ToArray());
        var card = new ScriptedDescrambler(_ => ++reads is 1 ? [7] : []);

        using var device = new DescramblingTunerDevice(source, card);

        Assert.Equal([7], device.Read(1024 * 1024, CancellationToken.None));

        Assert.Throws<EndOfTheScriptException>(
            () => device.Read(1024 * 1024, CancellationToken.None));
    }

    [Fact]
    public void WhatTheDescramblerStillHeldAtTheEndIsHandedOverWithTheTunersOwnTail()
    {
        var source = new ChunkByChunkTunerDevice([]) { Tail = [1] };
        var card = new ScriptedDescrambler(scrambled => [.. scrambled.ToArray()]) { Held = [9] };

        using var device = new DescramblingTunerDevice(source, card);

        Assert.Equal([1, 9], device.WhatIsHeldBack());
    }

    [Fact]
    public void ATunerThatWasNeverWrappedHoldsNothingBack()
    {
        var source = new ChunkByChunkTunerDevice([]);

        Assert.Empty(((ITunerDevice)source).WhatIsHeldBack());
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

    public byte[] Tail { get; set; } = [];

    public int Reads => next;

    public bool Disposed { get; private set; }

    public byte[] Read(int count, CancellationToken cancellationToken) =>
        next < chunks.Count ? chunks[next++] : throw new EndOfTheScriptException();

    public byte[] WhatIsHeldBack() => Tail;

    public void Dispose() => Disposed = true;
}

public delegate byte[] Unlocking(ReadOnlySpan<byte> stream);

public sealed class ScriptedDescrambler(Unlocking unlock) : IDescrambler
{
    public byte[] Held { get; set; } = [];

    public bool Disposed { get; private set; }

    public byte[] Descramble(ReadOnlySpan<byte> stream) => unlock(stream);

    public byte[] WhatIsStillHeld() => Held;

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

public sealed class DescrambledTailTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WhatTheDescramblerWasStillHoldingWhenTheStreamEndedIsInTheFile()
    {
        string directory = Directory.CreateTempSubdirectory("carina-tail").FullName;

        try
        {
            var device = new ChunkByChunkTunerDevice([[1, 2, 3]]) { Tail = [4, 5] };
            var writer = new RecordingWriter(directory, "0123456789abcdef0123456789abcdef");

            using (
                var session = new TunerSession(
                    SessionId.Parse("tail"),
                    SessionPurpose.Recording,
                    "adapter0",
                    device,
                    Start,
                    Start.AddHours(1),
                    new ManualTimeProvider(Start),
                    writer))
            {
                session.Start();
                await session.Completion.WaitAsync(TimeSpan.FromSeconds(10));

                Assert.Equal([1, 2, 3, 4, 5], File.ReadAllBytes(writer.Path));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ATunerThatHeldNothingBackAddsNothingToTheFile()
    {
        string directory = Directory.CreateTempSubdirectory("carina-tail").FullName;

        try
        {
            var device = new ChunkByChunkTunerDevice([[1, 2, 3]]);
            var writer = new RecordingWriter(directory, "0123456789abcdef0123456789abcdef");

            using (
                var session = new TunerSession(
                    SessionId.Parse("tail"),
                    SessionPurpose.Recording,
                    "adapter0",
                    device,
                    Start,
                    Start.AddHours(1),
                    new ManualTimeProvider(Start),
                    writer))
            {
                session.Start();
                await session.Completion.WaitAsync(TimeSpan.FromSeconds(10));

                Assert.Equal([1, 2, 3], File.ReadAllBytes(writer.Path));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
