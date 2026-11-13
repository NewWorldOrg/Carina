using System.Reflection;

using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class CardRateControlTests
{
    [Fact(DisplayName = "the card is handed a quantiser for H.264 and H.265 and a quality index for AV1")]
    public void TheCardIsHandedAQuantiserForH264AndH265AndAQualityIndexForAv1()
    {
        CardRateControl handed = new(new ConstantQuantiser(24), new ConstantQuantiser(26), new QualityIndex(80));

        Assert.Equal(new ConstantQuantiser(24), handed.H264);
        Assert.Equal(new ConstantQuantiser(26), handed.H265);
        Assert.Equal(new QualityIndex(80), handed.Av1);
        Assert.Throws<ArgumentNullException>(() => new CardRateControl(null!, new ConstantQuantiser(24), new QualityIndex(80)));
        Assert.Throws<ArgumentNullException>(() => new CardRateControl(new ConstantQuantiser(24), null!, new QualityIndex(80)));
        Assert.Throws<ArgumentNullException>(() => new CardRateControl(new ConstantQuantiser(24), new ConstantQuantiser(24), null!));
    }

    [Fact]
    public void NothingTheCardIsHandedCanBeChangedOnceItIsMade()
    {
        Assert.All(
            typeof(CardRateControl).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Concat(typeof(QualityIndex).GetProperties(BindingFlags.Public | BindingFlags.Instance)),
            property => Assert.Null(property.SetMethod));
    }
}
