using Carina.Domain.Machines;
using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class LiveEncoderChoiceTests
{
    [Theory]
    [InlineData(LiveEncoder.Software)]
    [InlineData(LiveEncoder.Vaapi)]
    [InlineData(LiveEncoder.Nvenc)]
    public void AnEncoderThatWasAskedForCarriesNoReasonToHaveFallenBack(LiveEncoder encoder)
    {
        LiveEncoderChoice chosen = LiveEncoderChoice.Asked(encoder);

        Assert.Equal(encoder, chosen.Encoder);
        Assert.False(chosen.FellBack);
        Assert.Null(chosen.FellBackBecause);
        Assert.Equal(string.Empty, chosen.Note);
    }

    [Fact]
    public void AnEncoderThatIsNotOneOfThoseNamedCannotBeChosen()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveEncoderChoice.Asked((LiveEncoder)7));
    }

    [Theory]
    [InlineData(CardStanding.NodeMissing)]
    [InlineData(CardStanding.NodeUnreadable)]
    [InlineData(CardStanding.DriverUnusable)]
    [InlineData(CardStanding.ProbeTimedOut)]
    [InlineData(CardStanding.ProbeProgrammeMissing)]
    public void FallingBackLandsOnSoftwareAndSaysWhy(CardStanding because)
    {
        LiveEncoderChoice chosen = LiveEncoderChoice.FellBackToSoftware(because, "the card said no");

        Assert.Equal(LiveEncoder.Software, chosen.Encoder);
        Assert.True(chosen.FellBack);
        Assert.Equal(because, chosen.FellBackBecause);
        Assert.Equal("the card said no", chosen.Note);
    }

    [Fact]
    public void FallingBackWhenTheCardIsFineIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LiveEncoderChoice.FellBackToSoftware(CardStanding.Usable, "silently"));
    }

    [Fact]
    public void WhyItFellBackCarriesNoPathFromThisMachine()
    {
        LiveEncoderChoice chosen = LiveEncoderChoice.FellBackToSoftware(
            CardStanding.DriverUnusable,
            "No VA display found for device /dev/dri/renderD128.");

        Assert.NotEmpty(chosen.Note);
        Assert.DoesNotContain('/', chosen.Note);
    }

    [Fact]
    public void TheOnlyReasonsACardIsTurnedDownAreThese()
    {
        Assert.Equal(
            [
                CardStanding.NodeMissing,
                CardStanding.NodeUnreadable,
                CardStanding.DriverUnusable,
                CardStanding.ProbeTimedOut,
                CardStanding.ProbeProgrammeMissing,
            ],
            Enum.GetValues<CardStanding>().Where(standing => !CardStandings.IsUsable(standing)));
    }
}
