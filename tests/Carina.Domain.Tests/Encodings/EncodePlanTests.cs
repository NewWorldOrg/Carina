using Carina.Domain.Encodings;
using Carina.Domain.Machines;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodePlanTests
{
    private static readonly DateTime At = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// What the container actually has, measured on 2026-09-05: ffmpeg 6.1.6 built with
    /// --enable-libx264 and --enable-vaapi and no libx265, so H.265 exists on the card alone.
    /// </summary>
    private static MachineCapabilities AsThisMachineIs => MachineCapabilities.Of(
        CardStanding.Usable,
        [
            Faculty.EncodeH264OnTheProcessor,
            Faculty.EncodeH264OnTheCard,
            Faculty.EncodeH265OnTheCard,
            Faculty.DecodeAribCaptions,
        ],
        string.Empty);

    private static MachineCapabilities WithNoCard => MachineCapabilities.Of(
        CardStanding.NodeMissing,
        [Faculty.EncodeH264OnTheProcessor, Faculty.DecodeAribCaptions],
        "no render node was handed to this container");

    [Fact(DisplayName = "BR-EV-004: what was asked for is what runs when this machine can do it")]
    public void WhatWasAskedForIsWhatRunsWhenThisMachineCanDoIt()
    {
        EncodePlan plan = EncodePlans.For(Profile(EncodeCodec.H264), EncodeEncoder.Vaapi, AsThisMachineIs);

        Assert.True(plan.CanRun);
        Assert.Equal(EncodeEncoder.Vaapi, plan.Encoder);
        Assert.Null(plan.Swerved);
        Assert.Null(plan.Refused);
    }

    [Fact(DisplayName = "BR-EV-004: a card that is out of reach is not a refusal, it is the processor instead")]
    public void ACardThatIsOutOfReachIsNotARefusalItIsTheProcessorInstead()
    {
        EncodePlan plan = EncodePlans.For(Profile(EncodeCodec.H264), EncodeEncoder.Vaapi, WithNoCard);

        Assert.True(plan.CanRun);
        Assert.Equal(EncodeEncoder.Software, plan.Encoder);
        Assert.Equal(EncodeSwerve.TheCardIsOutOfReach, plan.Swerved);
        Assert.Contains("no render node", plan.Note, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-EV-004: a build with no libx265 sends H.265 to the card rather than failing it")]
    public void ABuildWithNoSoftwareH265SendsItToTheCard()
    {
        EncodePlan plan = EncodePlans.For(Profile(EncodeCodec.H265), EncodeEncoder.Software, AsThisMachineIs);

        Assert.True(plan.CanRun);
        Assert.Equal(EncodeEncoder.Vaapi, plan.Encoder);
        Assert.Equal(EncodeSwerve.TheProcessorCannotDoThisCodec, plan.Swerved);
    }

    [Fact(DisplayName = "BR-EV-004: a codec neither the processor nor the card can do is the one thing that refuses")]
    public void ACodecNeitherSideCanDoIsTheOneThingThatRefuses()
    {
        EncodePlan plan = EncodePlans.For(Profile(EncodeCodec.H265), EncodeEncoder.Software, WithNoCard);

        Assert.False(plan.CanRun);
        Assert.Null(plan.Encoder);
        Assert.Equal(EncodeFailure.CapabilityUnavailable, plan.Refused);
        Assert.NotEmpty(plan.Note);
    }

    [Fact(DisplayName = "BR-EV-004: a card that cannot do the codec falls to the processor, not to a refusal")]
    public void ACardThatCannotDoTheCodecFallsToTheProcessor()
    {
        MachineCapabilities cardWithoutH265 = MachineCapabilities.Of(
            CardStanding.Usable,
            [Faculty.EncodeH264OnTheProcessor, Faculty.EncodeH265OnTheProcessor, Faculty.EncodeH264OnTheCard],
            string.Empty);

        EncodePlan plan = EncodePlans.For(Profile(EncodeCodec.H265), EncodeEncoder.Vaapi, cardWithoutH265);

        Assert.True(plan.CanRun);
        Assert.Equal(EncodeEncoder.Software, plan.Encoder);
        Assert.Equal(EncodeSwerve.TheCardCannotDoThisCodec, plan.Swerved);
    }

    [Fact(DisplayName = "BR-EV-004: a card that is there and a codec neither side can do still refuses")]
    public void ACardThatIsThereAndACodecNeitherSideCanDoStillRefuses()
    {
        MachineCapabilities neitherDoesH265 = MachineCapabilities.Of(
            CardStanding.Usable,
            [Faculty.EncodeH264OnTheProcessor, Faculty.EncodeH264OnTheCard],
            string.Empty);

        EncodePlan plan = EncodePlans.For(Profile(EncodeCodec.H265), EncodeEncoder.Vaapi, neitherDoesH265);

        Assert.False(plan.CanRun);
        Assert.Equal(EncodeFailure.CapabilityUnavailable, plan.Refused);
    }

    [Fact(DisplayName = "BR-EV-004: the card is not asked for when the processor was, and can do it")]
    public void TheCardIsNotAskedForWhenTheProcessorWasAndCanDoIt()
    {
        EncodePlan plan = EncodePlans.For(Profile(EncodeCodec.H264), EncodeEncoder.Software, AsThisMachineIs);

        Assert.Equal(EncodeEncoder.Software, plan.Encoder);
        Assert.Null(plan.Swerved);
    }

    [Fact]
    public void ASwerveNamesWhereItSwervedToAndWhy()
    {
        EncodePlan plan = EncodePlan.Swerving(EncodeEncoder.Software, EncodeSwerve.TheCardIsOutOfReach, "gone");

        Assert.Equal(EncodeEncoder.Software, plan.Encoder);
        Assert.Equal(EncodeSwerve.TheCardIsOutOfReach, plan.Swerved);
        Assert.Null(plan.Refused);
        Assert.True(plan.CanRun);
    }

    [Fact]
    public void APlanThatRefusesNamesNoEncoderToRunOn()
    {
        EncodePlan plan = EncodePlan.NothingHereCanDoIt("neither side has it");

        Assert.False(plan.CanRun);
        Assert.Null(plan.Encoder);
        Assert.Null(plan.Swerved);
        Assert.Equal(EncodeFailure.CapabilityUnavailable, plan.Refused);
    }

    [Fact]
    public void WhatAPlanSaysNamesNoPathOnThisMachine()
    {
        EncodePlan plan = EncodePlan.Swerving(
            EncodeEncoder.Software,
            EncodeSwerve.TheCardIsOutOfReach,
            "could not open /dev/dri/renderD128");

        Assert.DoesNotContain('/', plan.Note);
    }

    [Fact]
    public void AnEncoderNobodyOffersIsNotSomethingToSwerveTo()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => EncodePlan.Swerving((EncodeEncoder)9, EncodeSwerve.TheCardIsOutOfReach, string.Empty));

    [Fact]
    public void AReasonNobodyNamedIsNotAReasonToSwerve()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => EncodePlan.Swerving(EncodeEncoder.Software, (EncodeSwerve)9, string.Empty));

    [Fact]
    public void APlanSwervesForOneOfThreeReasons()
        => Assert.Equal(
            [
                EncodeSwerve.TheCardIsOutOfReach,
                EncodeSwerve.TheCardCannotDoThisCodec,
                EncodeSwerve.TheProcessorCannotDoThisCodec,
            ],
            Enum.GetValues<EncodeSwerve>());

    private static EncodeProfile Profile(EncodeCodec codec)
        => EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Standard"),
            codec,
            EncodeResolution.AsSource,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            At);
}
