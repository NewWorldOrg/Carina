using Carina.Driver.Descrambling;

namespace Carina.Driver.Tests;

public sealed class ConditionalAccessCardTests
{
    private static readonly byte[] SyntheticEcm = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A];

    [Fact]
    public void TheInitialSettingIsTheFirstThingSaidToTheCard()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection card);

        using ConditionalAccessCard opened = ConditionalAccessCard.Open(service);

        Assert.Equal([0x90, 0x30, 0x00, 0x00, 0x00], Assert.Single(card.Commands));
        Assert.Equal(SyntheticCardKeys.CaSystemId, opened.CaSystemId);
    }

    [Fact]
    public void AnEcmIsHandedOverWithItsLengthAndAnOpenAnswerLength()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        using ConditionalAccessCard opened = ConditionalAccessCard.Open(service);

        opened.Answer(SyntheticEcm);

        Assert.Equal(
            [0x90, 0x34, 0x00, 0x00, (byte)SyntheticEcm.Length, .. SyntheticEcm, 0x00],
            card.Commands[^1]
        );
    }

    [Theory]
    [InlineData(0x0200)]
    [InlineData(0x0400)]
    [InlineData(0x0800)]
    public void AGrantingReturnCodeHandsBackTheOddAndTheEvenKey(int returnCode)
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        card.EcmReturnCode = _ => (ushort)returnCode;
        using ConditionalAccessCard opened = ConditionalAccessCard.Open(service);

        EcmAnswer answer = opened.Answer(SyntheticEcm);

        Assert.True(answer.Granted);
        Assert.Equal(returnCode, answer.ReturnCode);
        AssertUnscramblesWith(answer.Keys!, SyntheticCardKeys.OddKeyFor(SyntheticEcm), withOddKey: true);
        AssertUnscramblesWith(answer.Keys!, SyntheticCardKeys.EvenKeyFor(SyntheticEcm), withOddKey: false);
    }

    [Theory]
    [InlineData(0xA102)]
    [InlineData(0xA103)]
    [InlineData(0x0000)]
    public void AnyOtherReturnCodeHandsBackNoKeys(int returnCode)
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        card.EcmReturnCode = _ => (ushort)returnCode;
        using ConditionalAccessCard opened = ConditionalAccessCard.Open(service);

        EcmAnswer answer = opened.Answer(SyntheticEcm);

        Assert.False(answer.Granted);
        Assert.Null(answer.Keys);
        Assert.Equal(returnCode, answer.ReturnCode);
    }

    [Fact]
    public void TheFirstReaderWhoseCardAnswersIsTheOneUsed()
    {
        FakeSmartCardService service = new();
        service.Refusing("Empty Reader", SmartCardCodes.NoSmartCard);
        FakeCardConnection silent = service.Card("Other Card");
        silent.InitialReturnCode = 0x2101;
        FakeCardConnection answering = service.Card("Answering Card");

        using ConditionalAccessCard opened = ConditionalAccessCard.Open(service);
        opened.Answer(SyntheticEcm);

        Assert.True(silent.Disposed);
        Assert.False(answering.Disposed);
        Assert.Single(answering.EcmBodies);
        Assert.Empty(silent.EcmBodies);
    }

    [Fact]
    public void NoReaderAtAllIsAFailureAndTheServiceIsLetGo()
    {
        FakeSmartCardService service = new();

        DescramblingException failure = Assert.Throws<DescramblingException>(() => ConditionalAccessCard.Open(service));

        Assert.Contains("No smart-card reader", failure.Message, StringComparison.Ordinal);
        Assert.True(service.Disposed);
    }

    [Fact]
    public void AServiceThatCannotListItsReadersIsAFailureNamingWhy()
    {
        FakeSmartCardService service = new() { ListingFailure = SmartCardCodes.NoService };

        DescramblingException failure = Assert.Throws<DescramblingException>(() => ConditionalAccessCard.Open(service));

        Assert.Contains("0x8010001D", failure.Message, StringComparison.Ordinal);
        Assert.True(service.Disposed);
    }

    [Fact]
    public void WhenNoCardAnswersEveryReaderIsNamedByWhatWentWrong()
    {
        FakeSmartCardService service = new();
        service.Refusing("Held Reader", SmartCardCodes.SharingViolation);
        FakeCardConnection silent = service.Card("Other Card");
        silent.InitialReturnCode = 0x2101;

        DescramblingException failure = Assert.Throws<DescramblingException>(() => ConditionalAccessCard.Open(service));

        Assert.Contains("2 reader(s)", failure.Message, StringComparison.Ordinal);
        Assert.Contains("0x8010000B", failure.Message, StringComparison.Ordinal);
        Assert.Contains("0x2101", failure.Message, StringComparison.Ordinal);
        Assert.True(silent.Disposed);
        Assert.True(service.Disposed);
    }

    [Theory]
    [InlineData(SmartCardCodes.ResetCard)]
    [InlineData(SmartCardCodes.RemovedCard)]
    public void ACardResetBySomebodyElseIsReconnectedSettledAgainAndAskedOnceMore(uint code)
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        using ConditionalAccessCard opened = ConditionalAccessCard.Open(service);
        card.Failures.Enqueue(code);

        EcmAnswer answer = opened.Answer(SyntheticEcm);

        Assert.True(answer.Granted);
        Assert.Equal(1, card.Reconnects);
        Assert.Equal([0x30, 0x34, 0x30, 0x34], card.Commands.Select(command => command[1]));
    }

    [Fact]
    public void ACardThatIsResetAgainOnTheSecondAskIsAFailure()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        using ConditionalAccessCard opened = ConditionalAccessCard.Open(service);
        card.Failures.Enqueue(SmartCardCodes.ResetCard);
        card.Failures.Enqueue(SmartCardCodes.Success);
        card.Failures.Enqueue(SmartCardCodes.Success);
        card.Failures.Enqueue(SmartCardCodes.ResetCard);

        Assert.Throws<DescramblingException>(() => opened.Answer(SyntheticEcm));
    }

    [Fact]
    public void AReconnectThatFailsIsAFailureNamingWhy()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        using ConditionalAccessCard opened = ConditionalAccessCard.Open(service);
        card.Failures.Enqueue(SmartCardCodes.RemovedCard);
        card.Failures.Enqueue(SmartCardCodes.NoSmartCard);

        DescramblingException failure = Assert.Throws<DescramblingException>(() => opened.Answer(SyntheticEcm));

        Assert.Contains("0x8010000C", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnyOtherFailureToReachTheCardIsAFailureNamingWhy()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        using ConditionalAccessCard opened = ConditionalAccessCard.Open(service);
        card.Failures.Enqueue(0x80100016);

        DescramblingException failure = Assert.Throws<DescramblingException>(() => opened.Answer(SyntheticEcm));

        Assert.Contains("0x80100016", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, card.Reconnects);
    }

    [Fact]
    public void AnAnswerTooShortToHoldTheKeysIsAFailure()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        using ConditionalAccessCard opened = ConditionalAccessCard.Open(service);
        card.Override = sent => sent[1] is 0x34 ? [0x00, 0x00, 0x00, 0x34, 0x08, 0x00, 0x90, 0x00] : null;

        Assert.Throws<DescramblingException>(() => opened.Answer(SyntheticEcm));
    }

    [Fact]
    public void AShortAnswerThatRefusesIsStillARefusal()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        using ConditionalAccessCard opened = ConditionalAccessCard.Open(service);
        card.Override = sent => sent[1] is 0x34 ? [0x00, 0x00, 0x00, 0x34, 0xA1, 0x02, 0x90, 0x00] : null;

        EcmAnswer answer = opened.Answer(SyntheticEcm);

        Assert.False(answer.Granted);
        Assert.Equal(0xA102, answer.ReturnCode);
    }

    [Fact]
    public void AnAnswerThatDoesNotEndCompletedIsAFailure()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        using ConditionalAccessCard opened = ConditionalAccessCard.Open(service);
        card.Override = sent => sent[1] is 0x34 ? [.. new byte[25], 0x6A, 0x86] : null;

        DescramblingException failure = Assert.Throws<DescramblingException>(() => opened.Answer(SyntheticEcm));

        Assert.Contains("0x6A86", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void AnEcmBodyThatCannotBeSentInOneCommandIsRefused(int length)
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection _);
        using ConditionalAccessCard opened = ConditionalAccessCard.Open(service);

        Assert.Throws<ArgumentOutOfRangeException>(() => opened.Answer(new byte[length]));
    }

    [Fact]
    public void NothingTheCardSaidReachesAMessage()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        card.InitialReturnCode = 0x2101;

        DescramblingException failure = Assert.Throws<DescramblingException>(() => ConditionalAccessCard.Open(service));

        string[] secrets =
        [
            Convert.ToHexString(SyntheticCardKeys.SystemKey.AsSpan(0, 4)),
            Convert.ToHexString(SyntheticCardKeys.InitialValue.AsSpan(0, 4)),
            Convert.ToHexString(SyntheticCardKeys.CardId.AsSpan(0, 4)),
        ];
        foreach (string secret in secrets)
        {
            Assert.DoesNotContain(secret, failure.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void KeysDoNotWriteThemselvesOut()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection _);
        using ConditionalAccessCard opened = ConditionalAccessCard.Open(service);

        EcmAnswer answer = opened.Answer(SyntheticEcm);

        Assert.Equal(nameof(ScrambleKeys), answer.Keys!.ToString());
    }

    [Fact]
    public void LettingGoClosesTheConnectionAndTheService()
    {
        FakeSmartCardService service = FakeSmartCardService.WithOneCard(out FakeCardConnection card);
        ConditionalAccessCard opened = ConditionalAccessCard.Open(service);

        opened.Dispose();
        opened.Dispose();

        Assert.True(card.Disposed);
        Assert.True(service.Disposed);
        Assert.Throws<ObjectDisposedException>(() => opened.Answer(SyntheticEcm));
    }

    private static void AssertUnscramblesWith(ScrambleKeys keys, Multi2 expected, bool withOddKey)
    {
        byte[] plain = new byte[27];
        new Random(27).NextBytes(plain);
        byte[] payload = [.. plain];
        expected.EncryptPayload(payload, SyntheticCardKeys.InitialValueAsBlock);

        keys.Unscramble(payload, withOddKey);

        Assert.Equal(plain, payload);
    }
}
