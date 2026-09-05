using Carina.Domain.Machines;

namespace Carina.Domain.Tests.Machines;

public sealed class MachineCapabilityTests
{
    [Fact]
    public void AMachineIsAskedAboutTheFiveThingsAnEncodeCanNeed()
        => Assert.Equal(
            [
                Faculty.EncodeH264OnTheProcessor,
                Faculty.EncodeH265OnTheProcessor,
                Faculty.EncodeH264OnTheCard,
                Faculty.EncodeH265OnTheCard,
                Faculty.DecodeAribCaptions,
            ],
            Enum.GetValues<Faculty>());

    [Fact]
    public void ACardStandsInOneOfSixPlacesAndOnlyOneOfThemIsUsable()
    {
        Assert.Equal(
            [
                CardStanding.Usable,
                CardStanding.NodeMissing,
                CardStanding.NodeUnreadable,
                CardStanding.DriverUnusable,
                CardStanding.ProbeTimedOut,
                CardStanding.ProbeProgrammeMissing,
            ],
            Enum.GetValues<CardStanding>());

        Assert.Single(Enum.GetValues<CardStanding>(), CardStandings.IsUsable);
    }

    [Fact(DisplayName = "BR-EV-004: a card that cannot be reached cannot be listed as able to encode on")]
    public void ACardThatCannotBeReachedCannotBeListedAsAbleToEncodeOn()
        => Assert.Throws<ArgumentException>(() => MachineCapabilities.Of(
            CardStanding.NodeMissing,
            [Faculty.EncodeH264OnTheCard],
            "no render node was handed to this container"));

    [Fact]
    public void AMachineWithNoCardStillSaysWhatItsProcessorCanDo()
    {
        MachineCapabilities can = MachineCapabilities.Of(
            CardStanding.NodeMissing,
            [Faculty.EncodeH264OnTheProcessor, Faculty.DecodeAribCaptions],
            "no render node was handed to this container");

        Assert.False(can.CardIsUsable);
        Assert.True(can.Has(Faculty.EncodeH264OnTheProcessor));
        Assert.True(can.Has(Faculty.DecodeAribCaptions));
        Assert.False(can.Has(Faculty.EncodeH264OnTheCard));
    }

    [Fact]
    public void TheSameFacultyNamedTwiceIsTheSameFacultyOnce()
        => Assert.Equal(
            [Faculty.EncodeH264OnTheProcessor],
            MachineCapabilities
                .Of(CardStanding.NodeMissing, [Faculty.EncodeH264OnTheProcessor, Faculty.EncodeH264OnTheProcessor], string.Empty)
                .Faculties);

    [Fact]
    public void AFacultyNobodyNamedIsNotAFaculty()
        => Assert.Throws<ArgumentOutOfRangeException>(() => MachineCapabilities.Of(
            CardStanding.Usable,
            [(Faculty)99],
            string.Empty));

    [Fact]
    public void AStandingNobodyNamedIsNotAStanding()
        => Assert.Throws<ArgumentOutOfRangeException>(() => MachineCapabilities.Of((CardStanding)99, [], string.Empty));

    [Fact]
    public void WhatTheCardComplainedOfNamesNoPathOnThisMachine()
    {
        MachineCapabilities can = MachineCapabilities.Of(
            CardStanding.DriverUnusable,
            [],
            "No VA display found for device /dev/dri/renderD128.");

        Assert.DoesNotContain('/', can.Note);
        Assert.Contains("No VA display found", can.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void AUsableCardIsAskedAboutNothingElse()
    {
        MachineCapabilities can = MachineCapabilities.Of(
            CardStanding.Usable,
            [Faculty.EncodeH264OnTheCard, Faculty.EncodeH264OnTheProcessor],
            string.Empty);

        Assert.True(can.CardIsUsable);
        Assert.Equal(string.Empty, can.Note);
    }
}
