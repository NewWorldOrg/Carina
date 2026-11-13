using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Domain.Tests.Unit.Rules;

[Trait("Category", "Unit")]
public sealed class RuleTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime CarriedAt = new(2026, 9, 8, 5, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Noon = new(2026, 9, 17, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ARuleCanBeDraftedDisabledAndTurnedOnLater()
    {
        Rule rule = Draft(enabled: false);

        Assert.False(rule.Enabled);

        rule.Enable();
        Assert.True(rule.Enabled);

        rule.Disable();
        Assert.False(rule.Enabled);
    }

    [Fact]
    public void ARuleKeepsTheQueryVerbatim()
    {
        const string Asked = "keyword=%E3%83%89%E3%83%A9%E3%83%9E&genre=7&genre=9&type=terrestrial";

        Rule rule = Rule.Draft(
            RuleId.New(),
            "Drama",
            new RuleQuery(Asked),
            Priority.Default,
            true,
            Margin.None,
            Margin.None,
            Now);

        Assert.Equal(Asked, rule.Query.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("?keyword=a")]
    [InlineData("keyword=a#top")]
    [InlineData("keyword=a&&genre=7")]
    [InlineData("=7")]
    public void AQueryThatIsNotTheOneAProgrammeSearchCarriesIsRefused(string asked)
        => Assert.Throws<ArgumentException>(() => new RuleQuery(asked));

    [Fact]
    public void ARuleIsNamed()
    {
        Assert.Throws<ArgumentException>(() => Draft(name: " "));
        Assert.Throws<ArgumentException>(() => Draft(name: new string('x', Rule.NameMaxLength + 1)));
    }

    [Fact]
    public void RewritingARuleKeepsItsIdentityAndItsEnabledState()
    {
        Rule rule = Draft(enabled: false);
        RuleId id = rule.Id;

        rule.Rewrite(
            "Renamed",
            new RuleQuery("genre=9"),
            new Priority(30),
            Margin.OfSeconds(10),
            Margin.OfSeconds(20),
            true);

        Assert.Equal(id, rule.Id);
        Assert.False(rule.Enabled);
        Assert.Equal("Renamed", rule.Name);
        Assert.Equal(30, rule.Priority.Value);
        Assert.Equal(10, rule.MarginBefore.Seconds);
    }

    [Fact(DisplayName = "a rule that says nothing about encoding asks for one, which is what every rule did before one could say otherwise")]
    public void ARuleThatSaysNothingAsksForAnEncode() => Assert.True(Drafted().EncodeWhenRecorded);

    [Fact(DisplayName = "a rule can be drafted asking for no encode")]
    public void ARuleCanBeDraftedAskingForNoEncode()
        => Assert.False(Drafted(encodeWhenRecorded: false).EncodeWhenRecorded);

    [Fact(DisplayName = "rewriting a rule keeps whatever the rewrite asked for rather than the priority or the margins deciding it")]
    public void RewritingARuleKeepsWhatTheRewriteAskedFor()
    {
        Rule rule = Drafted();

        rule.Rewrite("Renamed", new RuleQuery("genre=9"), Priority.Default, Margin.None, Margin.None, false);

        Assert.False(rule.EncodeWhenRecorded);

        rule.Rewrite("Renamed", new RuleQuery("genre=9"), Priority.Default, Margin.None, Margin.None, true);

        Assert.True(rule.EncodeWhenRecorded);
    }

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

        Rule carried = Rule.CarriedOver(id, "hill", query, CarriedAt);

        Assert.Equal(id, carried.Id);
        Assert.Equal("hill", carried.Name);
        Assert.Equal(query, carried.Query);
        Assert.Equal(CarriedAt, carried.CreatedAt);
    }

    [Fact(DisplayName = "a rule carried over encodes what it records, as any rule does until someone says otherwise")]
    public void ARuleCarriedOverEncodesWhatItRecords()
        => Assert.True(Carried().EncodeWhenRecorded);

    private static Rule Draft(string name = "Drama", bool enabled = true)
        => Rule.Draft(
            RuleId.New(),
            name,
            new RuleQuery("keyword=drama"),
            Priority.Default,
            enabled,
            Margin.None,
            Margin.None,
            Now);

    private static Rule Drafted(bool encodeWhenRecorded = true)
        => Rule.Draft(
            RuleId.New(),
            "a rule",
            new RuleQuery("keyword=hill"),
            Priority.Default,
            true,
            Margin.None,
            Margin.None,
            Noon,
            encodeWhenRecorded);

    private static Rule Carried() => Rule.CarriedOver(RuleId.New(), "hill", new RuleQuery("keyword=hill"), CarriedAt);
}
