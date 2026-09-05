using System.Reflection;

using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeProfileTests
{
    private static readonly DateTime At = new(2026, 9, 4, 3, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-EV-004: the slot the card reads takes a quantiser, and the processor's takes a rate factor")]
    public void EachEncoderHasASlotOfItsOwnAndTheTypeSaysWhichRateControlGoesInIt()
    {
        Assert.Equal(
            typeof(ConstantQuantiser),
            typeof(EncodeProfile).GetProperty(nameof(EncodeProfile.VaapiRateControl))!.PropertyType);

        Assert.Equal(
            typeof(ConstantRateFactor),
            typeof(EncodeProfile).GetProperty(nameof(EncodeProfile.SoftwareRateControl))!.PropertyType);
    }

    [Fact]
    public void AProfileCannotBeMadeWithoutGoingThroughTheOneWayIn()
    {
        Assert.Empty(typeof(EncodeProfile).GetConstructors());
    }

    [Fact]
    public void NoPartOfAProfileCanBeMovedFromOutside()
    {
        Assert.DoesNotContain(
            typeof(EncodeProfile).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.SetMethod is { IsPublic: true });
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(52)]
    public void ARateFactorOutsideTheScaleIsNotARateFactor(int rateFactor)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new ConstantRateFactor(rateFactor));

    [Theory]
    [InlineData(-1)]
    [InlineData(52)]
    public void AQuantiserOutsideTheScaleIsNotAQuantiser(int quantiser)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new ConstantQuantiser(quantiser));

    [Fact]
    public void ACodecNobodyOffersIsNotACodec()
        => Assert.Throws<ArgumentOutOfRangeException>(() => EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Standard"),
            (EncodeCodec)7,
            EncodeResolution.AsSource,
            Deinterlace.Leave,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            At));

    [Fact]
    public void ATimeThatIsNotInUtcIsNotATime()
        => Assert.Throws<ArgumentException>(() => EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Standard"),
            EncodeCodec.H264,
            EncodeResolution.AsSource,
            Deinterlace.Leave,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            new DateTime(2026, 9, 4, 3, 0, 0, DateTimeKind.Local)));

    [Fact]
    public void ADefinedProfileKeepsWhatItWasDefinedWith()
    {
        EncodeProfile profile = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Standard"),
            EncodeCodec.H264,
            EncodeResolution.AsSource,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            At);

        Assert.Equal(EncodeCodec.H264, profile.Codec);
        Assert.Equal(EncodeResolution.AsSource, profile.Resolution);
        Assert.Equal(Deinterlace.EveryFrame, profile.Deinterlace);
        Assert.Equal(22, profile.SoftwareRateControl.RateFactor);
        Assert.Equal(24, profile.VaapiRateControl.Quantiser);
        Assert.Equal(At, profile.DefinedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ALabelThatSaysNothingIsNotALabel(string label)
        => Assert.Throws<ArgumentException>(() => new EncodeLabel(label));

    [Fact]
    public void ALabelLongerThanTheColumnIsNotALabel()
        => Assert.Throws<ArgumentException>(() => new EncodeLabel(new string('x', EncodeLabel.Longest + 1)));

    [Fact]
    public void ALabelCarryingSomethingThatIsNotWritingIsNotALabel()
        => Assert.Throws<ArgumentException>(() => new EncodeLabel("Stan\u0007dard"));
}
