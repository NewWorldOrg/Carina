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

    [Fact(DisplayName = "BR-EV-006: a change replaces every field the definition was made of")]
    public void AChangeReplacesEveryFieldTheDefinitionWasMadeOf()
    {
        EncodeProfile profile = Standard();

        profile.Revise(
            new EncodeLabel("Finer"),
            EncodeCodec.H265,
            EncodeResolution.Hd,
            Deinterlace.Leave,
            new ConstantRateFactor(18),
            new ConstantQuantiser(20));

        Assert.Equal("Finer", profile.Label.Value);
        Assert.Equal(EncodeCodec.H265, profile.Codec);
        Assert.Equal(EncodeResolution.Hd, profile.Resolution);
        Assert.Equal(Deinterlace.Leave, profile.Deinterlace);
        Assert.Equal(18, profile.SoftwareRateControl.RateFactor);
        Assert.Equal(20, profile.VaapiRateControl.Quantiser);
        Assert.Equal(At, profile.DefinedAt);
    }

    [Fact(DisplayName = "BR-EV-006: a change to a codec nobody offers leaves the profile as it stood")]
    public void AChangeToACodecNobodyOffersLeavesTheProfileAsItStood()
    {
        EncodeProfile profile = Standard();

        Assert.Throws<ArgumentOutOfRangeException>(() => profile.Revise(
            new EncodeLabel("Finer"),
            (EncodeCodec)7,
            EncodeResolution.Hd,
            Deinterlace.Leave,
            new ConstantRateFactor(18),
            new ConstantQuantiser(20)));

        Assert.Equal("Standard", profile.Label.Value);
        Assert.Equal(EncodeCodec.H264, profile.Codec);
        Assert.Equal(EncodeResolution.AsSource, profile.Resolution);
    }

    [Fact(DisplayName = "BR-ED2-015: a profile that has not been retired says so, and says when once it has")]
    public void AProfileSaysWhenItWasRetired()
    {
        EncodeProfile profile = Standard();

        Assert.False(profile.IsRetired);
        Assert.Null(profile.RetiredAt);

        profile.Retire(At.AddDays(1));

        Assert.True(profile.IsRetired);
        Assert.Equal(At.AddDays(1), profile.RetiredAt);
    }

    [Fact(DisplayName = "BR-ED2-015: a retired profile is neither changed nor retired a second time")]
    public void ARetiredProfileIsNeitherChangedNorRetiredASecondTime()
    {
        EncodeProfile profile = Standard();
        profile.Retire(At.AddDays(1));

        Assert.Throws<InvalidOperationException>(() => profile.Revise(
            new EncodeLabel("Finer"),
            EncodeCodec.H264,
            EncodeResolution.Hd,
            Deinterlace.Leave,
            new ConstantRateFactor(18),
            new ConstantQuantiser(20)));
        Assert.Throws<InvalidOperationException>(() => profile.Retire(At.AddDays(2)));
        Assert.Equal(At.AddDays(1), profile.RetiredAt);
    }

    [Fact]
    public void AnHourThatIsNotInUtcIsNotWhenAProfileWasRetired()
        => Assert.Throws<ArgumentException>(() => Standard().Retire(new DateTime(2026, 9, 5, 3, 0, 0, DateTimeKind.Local)));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ALabelThatSaysNothingIsNotALabel(string label)
        => Assert.Throws<ArgumentException>(() => new EncodeLabel(label));

    private static EncodeProfile Standard()
        => EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Standard"),
            EncodeCodec.H264,
            EncodeResolution.AsSource,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            At);

    [Fact]
    public void ALabelLongerThanTheColumnIsNotALabel()
        => Assert.Throws<ArgumentException>(() => new EncodeLabel(new string('x', EncodeLabel.Longest + 1)));

    [Fact]
    public void ALabelCarryingSomethingThatIsNotWritingIsNotALabel()
        => Assert.Throws<ArgumentException>(() => new EncodeLabel("Stan\u0007dard"));
}
