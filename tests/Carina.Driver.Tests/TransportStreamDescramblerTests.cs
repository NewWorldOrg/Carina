using System.Text;

using Carina.Driver.Descrambling;

namespace Carina.Driver.Tests;

public sealed class TransportStreamDescramblerTests
{
    private const int PmtPid = 0x0101;

    private const int OtherPmtPid = 0x0102;

    private const int EcmPid = 0x0901;

    private const int OtherEcmPid = 0x0902;

    private const int VideoPid = 0x0111;

    private const int AudioPid = 0x0112;

    private const int OtherVideoPid = 0x0121;

    private static readonly byte[] FirstEcm = Encoding.ASCII.GetBytes("synthetic ecm one");

    private static readonly byte[] SecondEcm = Encoding.ASCII.GetBytes("second synthetic ecm");

    private static readonly byte[] OtherEcm = Encoding.ASCII.GetBytes("another synthetic ecm");

    [Fact]
    public void ScrambledPacketsBeforeTheFirstEcmAreHeldAndUnscrambledWithItsKeys()
    {
        SyntheticScrambledStream stream = new SyntheticScrambledStream()
            .Scrambled(VideoPid, FirstEcm, odd: false, seed: 1)
            .Pat((1, PmtPid))
            .Scrambled(VideoPid, FirstEcm, odd: true, seed: 2)
            .Pmt(PmtPid, 1, EcmPid, [(VideoPid, null), (AudioPid, null)])
            .Scrambled(AudioPid, FirstEcm, odd: false, seed: 3)
            .Ecm(EcmPid, FirstEcm)
            .Scrambled(VideoPid, FirstEcm, odd: true, seed: 4)
            .Scrambled(AudioPid, FirstEcm, odd: false, seed: 5);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection card);

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
        Assert.Single(card.EcmBodies);
    }

    [Fact]
    public void NothingIsHandedOnUntilTheHeadIsSettled()
    {
        SyntheticScrambledStream head = new SyntheticScrambledStream()
            .Pat((1, PmtPid))
            .Scrambled(VideoPid, FirstEcm, odd: false, seed: 1)
            .Pmt(PmtPid, 1, EcmPid, [(VideoPid, null)]);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection _);

        Assert.Empty(descrambler.Descramble(head.Input));
    }

    [Fact]
    public void AStreamWithNothingScrambledComesOutByteForByte()
    {
        SyntheticScrambledStream stream = new SyntheticScrambledStream()
            .Pat((1, PmtPid))
            .Clear(VideoPid, 1)
            .Pmt(PmtPid, 1, null, [(VideoPid, null)])
            .Clear(VideoPid, 2)
            .Clear(0x1FFF, 3);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection card);

        Assert.Equal(stream.Input, descrambler.Descramble(stream.Input));
        Assert.Empty(card.EcmBodies);
    }

    [Fact]
    public void AChangedEcmIsAskedAboutAndItsKeysTakeOverFromThePacketAfterIt()
    {
        SyntheticScrambledStream stream = Settled()
            .Scrambled(VideoPid, FirstEcm, odd: true, seed: 10)
            .Ecm(EcmPid, FirstEcm)
            .Scrambled(VideoPid, FirstEcm, odd: false, seed: 11)
            .Ecm(EcmPid, SecondEcm)
            .Scrambled(VideoPid, SecondEcm, odd: true, seed: 12)
            .Scrambled(VideoPid, SecondEcm, odd: false, seed: 13);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection card);

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
        Assert.Equal([FirstEcm, SecondEcm], card.EcmBodies);
    }

    [Fact]
    public void EveryProgrammeIsUnscrambledWithTheEcmItsPmtNamesAndAStreamsOwnEcmComesFirst()
    {
        SyntheticScrambledStream stream = new SyntheticScrambledStream()
            .Pat((1, PmtPid), (2, OtherPmtPid))
            .Pmt(PmtPid, 1, EcmPid, [(VideoPid, null), (AudioPid, OtherEcmPid)])
            .Pmt(OtherPmtPid, 2, OtherEcmPid, [(OtherVideoPid, null)])
            .Ecm(EcmPid, FirstEcm)
            .Ecm(OtherEcmPid, OtherEcm)
            .Scrambled(VideoPid, FirstEcm, odd: false, seed: 1)
            .Scrambled(AudioPid, OtherEcm, odd: true, seed: 2)
            .Scrambled(OtherVideoPid, OtherEcm, odd: false, seed: 3);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection _);

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
    }

    [Fact]
    public void APidNoPmtNamesIsUnscrambledWithTheOnlyEcmTheStreamCarries()
    {
        SyntheticScrambledStream stream = Settled().Scrambled(0x0555, FirstEcm, odd: true, seed: 20);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection _);

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
    }

    [Fact]
    public void APacketOfAProgrammeWhosePmtHasNotArrivedWaitsForItsOwnEcm()
    {
        SyntheticScrambledStream stream = new SyntheticScrambledStream()
            .Pat((1, PmtPid), (2, OtherPmtPid))
            .Pmt(PmtPid, 1, EcmPid, [(VideoPid, null)])
            .Ecm(EcmPid, FirstEcm)
            .Scrambled(OtherVideoPid, OtherEcm, odd: false, seed: 23)
            .Pmt(OtherPmtPid, 2, OtherEcmPid, [(OtherVideoPid, null)])
            .Ecm(OtherEcmPid, OtherEcm)
            .Scrambled(VideoPid, FirstEcm, odd: false, seed: 24);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection _);

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
    }

    [Fact]
    public void APidNoPmtNamesIsLeftAloneWhenTheStreamCarriesTwoEcms()
    {
        SyntheticScrambledStream stream = new SyntheticScrambledStream()
            .Pat((1, PmtPid), (2, OtherPmtPid))
            .Pmt(PmtPid, 1, EcmPid, [(VideoPid, null)])
            .Pmt(OtherPmtPid, 2, OtherEcmPid, [(OtherVideoPid, null)])
            .Ecm(EcmPid, FirstEcm)
            .Ecm(OtherEcmPid, OtherEcm)
            .LeftScrambled(0x0555, FirstEcm, odd: true, seed: 21);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection _);

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
    }

    [Fact]
    public void ADescriptorForAnotherConditionalAccessSystemNamesNoEcm()
    {
        SyntheticScrambledStream stream = new SyntheticScrambledStream()
            .Pat((1, PmtPid))
            .Pmt(PmtPid, 1, EcmPid, [(VideoPid, null)], caSystemId: 0x0123)
            .Ecm(EcmPid, FirstEcm)
            .LeftScrambled(VideoPid, FirstEcm, odd: false, seed: 22);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection card);

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
        Assert.Empty(card.EcmBodies);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(20)]
    [InlineData(178)]
    public void OnlyThePayloadAfterTheAdaptationFieldIsUnscrambledShortTailIncluded(int adaptationLength)
    {
        SyntheticScrambledStream stream = Settled()
            .Scrambled(VideoPid, FirstEcm, odd: true, seed: 30, adaptationLength: adaptationLength);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection _);

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
    }

    [Fact]
    public void APacketWithAnAdaptationFieldTooLongForItIsHandedOnUntouched()
    {
        SyntheticScrambledStream stream = Settled();
        byte[] broken = [.. stream.Header(VideoPid, unitStart: false, scrambling: 3, adaptation: 3), 184, .. new byte[183]];
        stream.Unchanged(broken);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection _);

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
    }

    [Fact]
    public void APacketMarkedAsDamagedInTransitIsHandedOnUntouched()
    {
        SyntheticScrambledStream stream = Settled();
        byte[] damaged = [.. stream.Header(VideoPid, unitStart: false, scrambling: 3, adaptation: 1), .. new byte[184]];
        damaged[1] |= 0x80;
        stream.Unchanged(damaged);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection _);

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
    }

    [Fact]
    public void AScrambledMarkOnAPacketWithNoPayloadIsCleared()
    {
        SyntheticScrambledStream stream = Settled();
        byte[] marked = [.. stream.Header(VideoPid, unitStart: false, scrambling: 2, adaptation: 2), 183, 0x00, .. Enumerable.Repeat((byte)0xFF, 182)];
        byte[] cleared = [.. marked];
        cleared[3] &= 0x3F;
        stream.Raw(marked, cleared);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection _);

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
    }

    [Fact]
    public void WhatTheCardRefusesStaysScrambledAndTheSameEcmIsNotAskedAgain()
    {
        SyntheticScrambledStream stream = new SyntheticScrambledStream()
            .Pat((1, PmtPid))
            .Pmt(PmtPid, 1, EcmPid, [(VideoPid, null)])
            .Ecm(EcmPid, FirstEcm)
            .LeftScrambled(VideoPid, FirstEcm, odd: true, seed: 40)
            .Ecm(EcmPid, FirstEcm)
            .LeftScrambled(VideoPid, FirstEcm, odd: false, seed: 41)
            .Ecm(EcmPid, SecondEcm)
            .Scrambled(VideoPid, SecondEcm, odd: true, seed: 42);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection card);
        card.EcmReturnCode = body => body.SequenceEqual(FirstEcm) ? (ushort)0xA102 : (ushort)0x0800;

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
        Assert.Equal([FirstEcm, SecondEcm], card.EcmBodies);
    }

    [Fact]
    public void AnEcmWhoseCrcDoesNotHoldIsNotHandedToTheCard()
    {
        SyntheticScrambledStream stream = Settled()
            .Ecm(EcmPid, SecondEcm, spoilCrc: true)
            .Scrambled(VideoPid, FirstEcm, odd: true, seed: 50);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection card);

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
        Assert.Equal([FirstEcm], card.EcmBodies);
    }

    [Fact]
    public void AnEcmThatSpansTwoPacketsIsGatheredBeforeItIsAsked()
    {
        byte[] longEcm = [.. Enumerable.Range(0, 230).Select(index => (byte)index)];
        SyntheticScrambledStream stream = new SyntheticScrambledStream()
            .Pat((1, PmtPid))
            .Pmt(PmtPid, 1, EcmPid, [(VideoPid, null)])
            .Ecm(EcmPid, longEcm)
            .Scrambled(VideoPid, longEcm, odd: false, seed: 60);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection card);

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
        Assert.Equal([longEcm], card.EcmBodies);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(187)]
    [InlineData(189)]
    [InlineData(4096)]
    public void ReadsThatDoNotEndOnAPacketGiveTheSameBytesInTheSameOrder(int chunk)
    {
        SyntheticScrambledStream stream = Settled();
        for (int seed = 0; seed < 40; seed++)
        {
            stream.Scrambled(seed % 2 is 0 ? VideoPid : AudioPid, FirstEcm, odd: seed % 3 is 0, seed: seed);
        }

        stream.Ecm(EcmPid, SecondEcm).Scrambled(VideoPid, SecondEcm, odd: true, seed: 99);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection _);
        byte[] input = stream.Input;
        List<byte> output = [];
        for (int offset = 0; offset < input.Length; offset += chunk)
        {
            output.AddRange(descrambler.Descramble(input.AsSpan(offset, Math.Min(chunk, input.Length - offset))));
        }

        Assert.Equal(stream.Output, output);
    }

    [Fact]
    public void BytesOutsideThePacketRhythmAreDropped()
    {
        SyntheticScrambledStream stream = Settled().Scrambled(VideoPid, FirstEcm, odd: true, seed: 70);
        byte[] input = stream.Input;
        int cut = input.Length - SyntheticScrambledStream.PacketLength;
        byte[] noisy = [0x00, 0x47, 0x12, .. input[..cut], 0x47, 0x00, 0x47, .. input[cut..]];

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection _);

        Assert.Equal(stream.Output, descrambler.Descramble(noisy));
    }

    [Fact]
    public void AHeadThatNeverSettlesIsHandedOnAsItStandsOnceTheHoldIsFull()
    {
        SyntheticScrambledStream stream = new SyntheticScrambledStream()
            .Pat((1, PmtPid), (2, OtherPmtPid))
            .Pmt(PmtPid, 1, EcmPid, [(VideoPid, null)])
            .Ecm(EcmPid, FirstEcm)
            .Scrambled(VideoPid, FirstEcm, odd: true, seed: 80)
            .Clear(OtherVideoPid, 81);

        using TransportStreamDescrambler descrambler = Open(
            out FakeCardConnection _,
            holdLimit: SyntheticScrambledStream.PacketLength * stream.PacketCount
        );

        Assert.Equal(stream.Output, descrambler.Descramble(stream.Input));
    }

    [Fact]
    public void WhenTheCardStopsWhatWasHeldBeforeThatReadIsHandedBackAndTheFailureRises()
    {
        SyntheticScrambledStream head = new SyntheticScrambledStream()
            .Pat((1, PmtPid))
            .Pmt(PmtPid, 1, EcmPid, [(VideoPid, null)]);
        byte[] firstRead = [.. head.Input, .. new SyntheticScrambledStream().Clear(VideoPid, 1).Input[..100]];
        SyntheticScrambledStream rest = new SyntheticScrambledStream().Ecm(EcmPid, FirstEcm);

        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection card);
        Assert.Empty(descrambler.Descramble(firstRead));
        card.Failures.Enqueue(0x80100016);

        Assert.Throws<DescramblingException>(() => descrambler.Descramble(rest.Input));
        Assert.Equal(firstRead, descrambler.WhatItCouldNotRead());
    }

    [Fact]
    public void AnEmptyReadGivesNothing()
    {
        using TransportStreamDescrambler descrambler = Open(out FakeCardConnection _);

        Assert.Empty(descrambler.Descramble([]));
    }

    [Fact]
    public void LettingGoClosesTheCard()
    {
        TransportStreamDescrambler descrambler = Open(out FakeCardConnection card);

        descrambler.Dispose();

        Assert.True(card.Disposed);
        Assert.Throws<ObjectDisposedException>(() => descrambler.Descramble([0x47]));
    }

    [Fact]
    public void TheSectionCheckIsTheMpegCrc()
    {
        Assert.Equal(0x0376E6E7u, PsiSection.Crc(Encoding.ASCII.GetBytes("123456789")));
    }

    private static SyntheticScrambledStream Settled() =>
        new SyntheticScrambledStream()
            .Pat((1, PmtPid))
            .Pmt(PmtPid, 1, EcmPid, [(VideoPid, null), (AudioPid, null)])
            .Ecm(EcmPid, FirstEcm);

    private static TransportStreamDescrambler Open(
        out FakeCardConnection card,
        int holdLimit = TransportStreamDescrambler.LongestHold
    )
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out card);

        return new TransportStreamDescrambler(ConditionalAccessCard.Open(service), holdLimit: holdLimit);
    }
}
