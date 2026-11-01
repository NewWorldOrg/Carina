namespace Carina.Contracts.Tests;

public sealed class CardReaderTests
{
    [Theory]
    [InlineData(CardReaderUse.Unspecified, "unspecified")]
    [InlineData(CardReaderUse.Local, "local")]
    [InlineData(CardReaderUse.Borrowed, "borrowed")]
    [InlineData(CardReaderUse.None, "none")]
    public void TheUseIsSpelledThisWay(CardReaderUse value, string wire)
    {
        Assert.Equal($"\"{wire}\"", DriverJson.Serialize(value, DriverJson.Context.CardReaderUse));
        Assert.Equal(value, DriverJson.Deserialize($"\"{wire}\"", DriverJson.Context.CardReaderUse));
    }

    [Theory]
    [InlineData(CardState.Unspecified, "unspecified")]
    [InlineData(CardState.Inserted, "inserted")]
    [InlineData(CardState.Empty, "empty")]
    [InlineData(CardState.Unresponsive, "unresponsive")]
    [InlineData(CardState.Borrowed, "borrowed")]
    [InlineData(CardState.NoReader, "noReader")]
    [InlineData(CardState.NotUsed, "notUsed")]
    public void TheStateIsSpelledThisWay(CardState value, string wire)
    {
        Assert.Equal($"\"{wire}\"", DriverJson.Serialize(value, DriverJson.Context.CardState));
        Assert.Equal(value, DriverJson.Deserialize($"\"{wire}\"", DriverJson.Context.CardState));
    }

    [Theory]
    [InlineData(CardProblem.Unspecified, "unspecified")]
    [InlineData(CardProblem.ServiceDown, "serviceDown")]
    [InlineData(CardProblem.LibraryMissing, "libraryMissing")]
    [InlineData(CardProblem.CardSilent, "cardSilent")]
    [InlineData(CardProblem.LenderUnreachable, "lenderUnreachable")]
    [InlineData(CardProblem.LenderRefused, "lenderRefused")]
    public void TheProblemIsSpelledThisWay(CardProblem value, string wire)
    {
        Assert.Equal($"\"{wire}\"", DriverJson.Serialize(value, DriverJson.Context.CardProblem));
        Assert.Equal(value, DriverJson.Deserialize($"\"{wire}\"", DriverJson.Context.CardProblem));
    }

    [Fact]
    public void EveryValueOfTheThreeHasASpellingOfItsOwn()
    {
        Assert.Equal(
            Enum.GetValues<CardState>().Length,
            Enum.GetValues<CardState>().Select(value => DriverJson.Serialize(value, DriverJson.Context.CardState)).Distinct().Count()
        );
        Assert.Equal(
            Enum.GetValues<CardProblem>().Length,
            Enum.GetValues<CardProblem>().Select(value => DriverJson.Serialize(value, DriverJson.Context.CardProblem)).Distinct().Count()
        );
        Assert.Equal(
            Enum.GetValues<CardReaderUse>().Length,
            Enum.GetValues<CardReaderUse>().Select(value => DriverJson.Serialize(value, DriverJson.Context.CardReaderUse)).Distinct().Count()
        );
    }

    [Fact]
    public void TheEndpointAndTheCapabilityAreSpelledThisWay()
    {
        Assert.Equal("/card-reader", DriverEndpoints.CardReader);
        Assert.Contains(DriverEndpoints.CardReader, DriverEndpoints.All);
        Assert.Equal("cardReaderStatus", DriverCapabilities.CardReaderStatus);
    }

    [Fact]
    public void TheAnswerKeepsExactlyTheseFieldsAndNoneNamesTheCard()
    {
        Assert.Equal(
            """{"use":"local","state":"inserted","problem":null,"readers":[{"name":"Example Reader","state":"inserted","inUse":true}],"lenderHostName":null,"leasesOut":null,"lastAnsweredAt":null,"checkedAt":null}""",
            DriverJson.Serialize(
                new CardReaderDto
                {
                    Use = CardReaderUse.Local,
                    State = CardState.Inserted,
                    Readers = [new CardReaderSlotDto { Name = "Example Reader", State = CardState.Inserted, InUse = true }],
                }
            )
        );
    }

    [Fact]
    public void AnAnswerWithValuesThisBuildDoesNotKnowStillReads()
    {
        CardReaderDto? reader = DriverJson.Deserialize(
            """{"use":"remote","state":"sleeping","problem":"cosmicRay","readers":null,"leasesOut":2}""",
            DriverJson.Context.CardReaderDto
        );

        Assert.NotNull(reader);
        Assert.Equal(CardReaderUse.Unspecified, reader.Use);
        Assert.Equal(CardState.Unspecified, reader.State);
        Assert.Equal(CardProblem.Unspecified, reader.Problem);
        Assert.Empty(reader.Readers);
        Assert.Equal(2, reader.LeasesOut);
    }
}
