using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Domain.Tests.Rules;

public sealed class RuleCarriedOverTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 5, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-MD-003: a rule carried over from the recording system being replaced arrives turned off, so it reserves nothing until someone turns it on")]
    public void ARuleCarriedOverArrivesTurnedOff()
    {
        Rule carried = Carried();

        Assert.False(carried.Enabled);
    }

    [Fact(DisplayName = "BR-MD-003: a rule carried over takes the default priority and no margin on either side, because the source system's own are not carried")]
    public void ARuleCarriedOverTakesTheDefaultPriorityAndNoMargins()
    {
        Rule carried = Carried();

        Assert.Equal(Priority.Default, carried.Priority);
        Assert.Equal(Margin.None, carried.MarginBefore);
        Assert.Equal(Margin.None, carried.MarginAfter);
    }

    [Fact(DisplayName = "BR-MD-003: a rule carried over keeps the name and the search it was converted to, and is dated the moment it was made")]
    public void ARuleCarriedOverKeepsWhatItWasConvertedTo()
    {
        RuleId id = RuleId.New();
        RuleQuery query = new("keyword=hill");

        Rule carried = Rule.CarriedOver(id, "hill", query, Now);

        Assert.Equal(id, carried.Id);
        Assert.Equal("hill", carried.Name);
        Assert.Equal(query, carried.Query);
        Assert.Equal(Now, carried.CreatedAt);
    }

    [Fact(DisplayName = "a rule carried over encodes what it records, as any rule does until someone says otherwise")]
    public void ARuleCarriedOverEncodesWhatItRecords()
        => Assert.True(Carried().EncodeWhenRecorded);

    private static Rule Carried() => Rule.CarriedOver(RuleId.New(), "hill", new RuleQuery("keyword=hill"), Now);
}
