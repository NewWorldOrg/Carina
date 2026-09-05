using System.Reflection;

using Carina.Contracts;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeValidationTests
{
    private static readonly DateTime At = new(2026, 9, 4, 3, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<StorageRootDto> Declared =
    [
        new() { Name = "primary", FreeBytes = 1, TotalBytes = 2, Writable = true },
        new() { Name = "spare", FreeBytes = 1, TotalBytes = 2, Writable = true },
    ];

    private static readonly IReadOnlyList<OutputRoot> Held = [new("primary"), new("spare")];

    private static EncodeProfileDraft Sound()
        => new("Standard", EncodeCodec.H264, EncodeResolution.AsSource, Deinterlace.EveryFrame, 22, 24);

    [Fact(DisplayName = "BR-EV-001: a save is refused a root the driver never declared")]
    public void ASaveIsRefusedARootTheDriverNeverDeclared()
    {
        EncodeProfileId known = EncodeProfileId.New();

        Assert.Equal(
            [EncodeRefusal.OutputRootNotDeclared],
            EncodeValidation.WhatRefusesTheDestination(
                new EncodeDestinationDraft("Where it goes", "somewhere-else", known),
                Declared,
                Held,
                [known]));
    }

    [Fact(DisplayName = "BR-EV-001: membership is by the exact name, not a near one")]
    public void MembershipIsByTheExactNameNotANearOne()
    {
        EncodeProfileId known = EncodeProfileId.New();

        foreach (string near in new[] { "PRIMARY", " primary", "primary ", "prim" })
        {
            Assert.Contains(
                EncodeRefusal.OutputRootNotDeclared,
                EncodeValidation.WhatRefusesTheDestination(
                    new EncodeDestinationDraft("Where it goes", near, known),
                    Declared,
                    Held,
                    [known]));
        }
    }

    [Fact(DisplayName = "BR-EV-001: a destination naming a declared root and a known profile is let through")]
    public void ADestinationNamingADeclaredRootAndAKnownProfileIsLetThrough()
    {
        EncodeProfileId known = EncodeProfileId.New();

        Assert.Empty(EncodeValidation.WhatRefusesTheDestination(
            new EncodeDestinationDraft("Where it goes", "primary", known),
            Declared,
            Held,
            [known]));
    }

    [Fact(DisplayName = "BR-EV-001: a declared root this process only reads from is refused, because an artefact is never placed in it")]
    public void ADeclaredRootThisProcessOnlyReadsFromIsRefused()
    {
        EncodeProfileId known = EncodeProfileId.New();

        Assert.Equal(
            [EncodeRefusal.OutputRootNotHeld],
            EncodeValidation.WhatRefusesTheDestination(
                new EncodeDestinationDraft("Where it goes", "primary", known),
                Declared,
                [new OutputRoot("spare")],
                [known]));
    }

    [Fact(DisplayName = "BR-EV-001: a root this process holds but nobody declared is refused as undeclared, not as unheld")]
    public void ARootThisProcessHoldsButNobodyDeclaredIsRefusedAsUndeclared()
    {
        EncodeProfileId known = EncodeProfileId.New();

        Assert.Equal(
            [EncodeRefusal.OutputRootNotDeclared],
            EncodeValidation.WhatRefusesTheDestination(
                new EncodeDestinationDraft("Where it goes", "encodes", known),
                Declared,
                [new OutputRoot("encodes")],
                [known]));
    }

    [Fact(DisplayName = "BR-EV-001: a destination pointing at a profile nobody defined is refused")]
    public void ADestinationPointingAtAProfileNobodyDefinedIsRefused()
    {
        Assert.Equal(
            [EncodeRefusal.DefaultProfileUnknown],
            EncodeValidation.WhatRefusesTheDestination(
                new EncodeDestinationDraft("Where it goes", "primary", EncodeProfileId.New()),
                Declared,
                Held,
                [EncodeProfileId.New()]));
    }

    [Fact(DisplayName = "BR-EV-001: nothing is let through when the driver declares nothing")]
    public void NothingIsLetThroughWhenTheDriverDeclaresNothing()
    {
        EncodeProfileId known = EncodeProfileId.New();

        Assert.Contains(
            EncodeRefusal.OutputRootNotDeclared,
            EncodeValidation.WhatRefusesTheDestination(
                new EncodeDestinationDraft("Where it goes", "primary", known),
                [],
                Held,
                [known]));
    }

    [Fact(DisplayName = "BR-EV-004: a sound profile is refused nothing")]
    public void ASoundProfileIsRefusedNothing()
        => Assert.Empty(EncodeValidation.WhatRefusesTheProfile(Sound()));

    [Fact(DisplayName = "BR-EV-004: a value cast in from outside the list is refused")]
    public void AValueCastInFromOutsideTheListIsRefused()
    {
        Assert.Equal(
            [EncodeRefusal.CodecUnknown],
            EncodeValidation.WhatRefusesTheProfile(Sound() with { Codec = (EncodeCodec)7 }));

        Assert.Equal(
            [EncodeRefusal.ResolutionUnknown],
            EncodeValidation.WhatRefusesTheProfile(Sound() with { Resolution = (EncodeResolution)7 }));

        Assert.Equal(
            [EncodeRefusal.DeinterlaceUnknown],
            EncodeValidation.WhatRefusesTheProfile(Sound() with { Deinterlace = (Deinterlace)7 }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(52)]
    public void ARateFactorOffTheScaleIsRefused(int rateFactor)
        => Assert.Equal(
            [EncodeRefusal.RateFactorOutOfRange],
            EncodeValidation.WhatRefusesTheProfile(Sound() with { RateFactor = rateFactor }));

    [Theory]
    [InlineData(-1)]
    [InlineData(52)]
    public void AQuantiserOffTheScaleIsRefused(int quantiser)
        => Assert.Equal(
            [EncodeRefusal.QuantiserOutOfRange],
            EncodeValidation.WhatRefusesTheProfile(Sound() with { Quantiser = quantiser }));

    [Fact(DisplayName = "BR-EV-004: every refusal a save can be given is written down here")]
    public void EveryRefusalASaveCanBeGivenIsWrittenDownHere()
        => Assert.Equal(
            [
                EncodeRefusal.CodecUnknown,
                EncodeRefusal.ResolutionUnknown,
                EncodeRefusal.DeinterlaceUnknown,
                EncodeRefusal.RateFactorOutOfRange,
                EncodeRefusal.QuantiserOutOfRange,
                EncodeRefusal.LabelMissing,
                EncodeRefusal.LabelTooLong,
                EncodeRefusal.OutputRootNotDeclared,
                EncodeRefusal.DefaultProfileUnknown,
                EncodeRefusal.OutputRootNotHeld,
            ],
            Enum.GetValues<EncodeRefusal>());

    [Fact(DisplayName = "BR-EV-004: no refusal names what the machine can or cannot do")]
    public void NoRefusalNamesWhatTheMachineCanOrCannotDo()
    {
        string[] capability = ["Vaapi", "RenderNode", "Ffmpeg", "Build", "Capability", "Device", "Driver"];

        Assert.All(
            Enum.GetNames<EncodeRefusal>(),
            refusal => Assert.DoesNotContain(
                capability,
                word => refusal.Contains(word, StringComparison.Ordinal)));
    }

    [Fact(DisplayName = "BR-EV-004: a save cannot be told what the machine can do, so it cannot refuse on it")]
    public void ASaveCannotBeToldWhatTheMachineCanDoSoItCannotRefuseOnIt()
    {
        Type[] handedIn =
        [
            .. typeof(EncodeValidation)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .SelectMany(method => method.GetParameters())
                .Select(parameter => parameter.ParameterType),
        ];

        Assert.Equal(
            [
                typeof(EncodeDestinationDraft),
                typeof(EncodeProfileDraft),
                typeof(IReadOnlyList<StorageRootDto>),
                typeof(IReadOnlyList<EncodeProfileId>),
                typeof(IReadOnlyList<OutputRoot>),
            ],
            handedIn.Distinct().OrderBy(type => type.ToString(), StringComparer.Ordinal));
    }

    [Fact(DisplayName = "BR-EV-004: an encoder the machine has no card for still makes a profile that saves")]
    public void AnEncoderTheMachineHasNoCardForStillMakesAProfileThatSaves()
    {
        Assert.Empty(EncodeValidation.WhatRefusesTheProfile(Sound()));

        EncodeProfile saved = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Standard"),
            EncodeCodec.H265,
            EncodeResolution.FullHd,
            Deinterlace.EveryField,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            At);

        Assert.Equal(EncodeCodec.H265, saved.Codec);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ALabelThatSaysNothingIsRefused(string? label)
    {
        Assert.Contains(
            EncodeRefusal.LabelMissing,
            EncodeValidation.WhatRefusesTheProfile(Sound() with { Label = label }));

        Assert.Contains(
            EncodeRefusal.LabelMissing,
            EncodeValidation.WhatRefusesTheDestination(
                new EncodeDestinationDraft(label, "primary", EncodeProfileId.New()),
                Declared,
                Held,
                []));
    }

    [Fact]
    public void ALabelLongerThanTheColumnIsRefused()
        => Assert.Contains(
            EncodeRefusal.LabelTooLong,
            EncodeValidation.WhatRefusesTheProfile(
                Sound() with { Label = new string('x', EncodeLabel.Longest + 1) }));

    [Fact]
    public void ADestinationHoldsTheRootAsANameRatherThanAPath()
        => Assert.Equal(
            typeof(OutputRoot),
            typeof(EncodeDestination).GetProperty(nameof(EncodeDestination.OutputRoot))!.PropertyType);
}
