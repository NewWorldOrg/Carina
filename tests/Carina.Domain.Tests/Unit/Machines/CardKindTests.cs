using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Machines;

[Trait("Category", "Unit")]
public sealed class CardKindTests
{
    [Fact]
    public void AMachineHasOneOfTwoKindsOfCard()
        => Assert.Equal([CardKind.Vaapi, CardKind.Nvenc], Enum.GetValues<CardKind>());

    [Fact(DisplayName = "the card the two settings agree on is the one either names, and VAAPI when neither names a card")]
    public void TheCardTheTwoSettingsAgreeOnIsTheOneEitherNames()
    {
        Assert.Equal(CardKind.Vaapi, CardKinds.Agreed(null, null));
        Assert.Equal(CardKind.Nvenc, CardKinds.Agreed(CardKind.Nvenc, null));
        Assert.Equal(CardKind.Nvenc, CardKinds.Agreed(null, CardKind.Nvenc));
        Assert.Equal(CardKind.Vaapi, CardKinds.Agreed(CardKind.Vaapi, null));
        Assert.Equal(CardKind.Nvenc, CardKinds.Agreed(CardKind.Nvenc, CardKind.Nvenc));
        Assert.Equal(CardKind.Vaapi, CardKinds.Agreed(CardKind.Vaapi, CardKind.Vaapi));
    }

    [Fact(DisplayName = "two settings naming different cards are refused, because a machine has one kind of card")]
    public void TwoSettingsNamingDifferentCardsAreRefused()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => CardKinds.Agreed(CardKind.Vaapi, CardKind.Nvenc));

        Assert.Contains("Vaapi", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Nvenc", refusal.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => CardKinds.Agreed(CardKind.Nvenc, CardKind.Vaapi));
    }

    [Fact]
    public void AKindNobodyNamedIsNotAKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CardKinds.Named((CardKind)9));
        Assert.Throws<ArgumentOutOfRangeException>(() => CardKinds.Agreed((CardKind)9, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => CardKinds.Agreed(null, (CardKind)9));
    }

    [Fact(DisplayName = "each encoder names the card it runs on, and the processor names none")]
    public void EachEncoderNamesTheCardItRunsOn()
    {
        Assert.Null(EncodeEncoders.CardOf(EncodeEncoder.Software));
        Assert.Equal(CardKind.Vaapi, EncodeEncoders.CardOf(EncodeEncoder.Vaapi));
        Assert.Equal(CardKind.Nvenc, EncodeEncoders.CardOf(EncodeEncoder.Nvenc));
        Assert.Null(LiveEncoders.CardOf(LiveEncoder.Software));
        Assert.Equal(CardKind.Vaapi, LiveEncoders.CardOf(LiveEncoder.Vaapi));
        Assert.Equal(CardKind.Nvenc, LiveEncoders.CardOf(LiveEncoder.Nvenc));
    }

    [Fact]
    public void EachCardIsRunOnByOneEncoder()
    {
        Assert.Equal(EncodeEncoder.Vaapi, EncodeEncoders.On(CardKind.Vaapi));
        Assert.Equal(EncodeEncoder.Nvenc, EncodeEncoders.On(CardKind.Nvenc));
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeEncoders.On((CardKind)9));
    }

    [Fact]
    public void TheProcessorIsTheOneEncoderThatIsNotACard()
    {
        Assert.False(EncodeEncoders.OnTheCard(EncodeEncoder.Software));
        Assert.True(EncodeEncoders.OnTheCard(EncodeEncoder.Vaapi));
        Assert.True(EncodeEncoders.OnTheCard(EncodeEncoder.Nvenc));
        Assert.False(LiveEncoders.OnTheCard(LiveEncoder.Software));
        Assert.True(LiveEncoders.OnTheCard(LiveEncoder.Vaapi));
        Assert.True(LiveEncoders.OnTheCard(LiveEncoder.Nvenc));
        Assert.Throws<ArgumentOutOfRangeException>(() => EncodeEncoders.OnTheCard((EncodeEncoder)9));
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveEncoders.OnTheCard((LiveEncoder)9));
    }

    [Fact]
    public void TheSettingsStartOnTheVaapiCard()
        => Assert.Equal(CardKind.Vaapi, new MachineSettings().Card);
}
