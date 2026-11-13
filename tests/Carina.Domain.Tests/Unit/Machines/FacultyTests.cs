using Carina.Domain.Machines;

namespace Carina.Domain.Tests.Unit.Machines;

[Trait("Category", "Unit")]
public sealed class FacultyTests
{
    [Fact]
    public void AMachineIsAskedAboutTheSixThingsAnEncodeOrATranscodeCanNeed()
        => Assert.Equal(
            [
                Faculty.EncodeH264OnTheProcessor,
                Faculty.EncodeH265OnTheProcessor,
                Faculty.EncodeH264OnTheCard,
                Faculty.EncodeH265OnTheCard,
                Faculty.DecodeAribCaptions,
                Faculty.EncodeAv1OnTheCard,
            ],
            Enum.GetValues<Faculty>());

    [Fact(DisplayName = "AV1 is encoded on the card and nowhere else, so a card that cannot be reached cannot be listed as encoding it")]
    public void Av1IsEncodedOnTheCardAndNowhereElse()
    {
        Assert.Equal(
            [Faculty.EncodeH264OnTheCard, Faculty.EncodeH265OnTheCard, Faculty.EncodeAv1OnTheCard],
            Faculties.OnTheCard);
        Assert.Throws<ArgumentException>(() => MachineCapabilities.Of(
            CardStanding.DriverUnusable,
            [Faculty.EncodeAv1OnTheCard],
            "refused"));
    }
}
