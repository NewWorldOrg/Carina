using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Programmes;
using Carina.Infrastructure.Rules;
using Carina.Infrastructure.Tests.Reservations;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Rules;

public sealed class RuleMatcherTests
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const int Network = 4;

    private const int Listed = 1049;

    private const int Beside = 1040;

    private const int Unlisted = 1032;

    [Fact]
    public async Task ARuleTakesTheProgrammesItsQueryAsksFor()
    {
        var streams = new HeldStreams([Terrestrial(Listed)]);
        RuleMatcher matcher = Matcher(streams);

        RuleMatchRun run = await matcher.AgainstAsync(
            [Written("keyword=hill", 10, 1)],
            Guide(
                Programme(Listed, 1, "hill walking"),
                Programme(Listed, 2, "the hill at dawn"),
                Programme(Listed, 3, "river fishing")),
            Cancel);

        Assert.Equal(["hill walking", "the hill at dawn"], Named(run));
        Assert.Empty(run.TurnedOff);
    }

    [Fact]
    public async Task ARuleThatIsOffTakesNothingThoughTheSameQueryWouldTakeThree()
    {
        var streams = new HeldStreams([Terrestrial(Listed)]);
        RuleMatcher matcher = Matcher(streams);
        IReadOnlyList<ProgrammeMatch> guide = Guide(
            Programme(Listed, 1, "hill walking"),
            Programme(Listed, 2, "the hill at dawn"),
            Programme(Listed, 3, "hillside"));

        RuleMatchRun taken = await matcher.AgainstAsync([Written("keyword=hill", 10, 1)], guide, Cancel);
        RuleMatchRun left = await matcher.AgainstAsync(
            [Written("keyword=hill", 10, 1, enabled: false)],
            guide,
            Cancel);

        Assert.Equal(3, taken.Matches.Count);
        Assert.Empty(left.Matches);
        Assert.Empty(left.TurnedOff);
    }

    [Fact]
    public async Task GenresAreReadOneOrTheOtherRatherThanAllTogether()
    {
        var streams = new HeldStreams([Terrestrial(Listed)]);
        RuleMatcher matcher = Matcher(streams);

        RuleMatchRun run = await matcher.AgainstAsync(
            [Written("genre=6&genre=8", 10, 1)],
            Guide(
                Filed(Listed, 1, "the news", 8),
                Filed(Listed, 2, "the play", 6),
                Filed(Listed, 3, "the game", 3)),
            Cancel);

        Assert.Equal(["the news", "the play"], Named(run));
    }

    [Fact]
    public async Task TheHigherPriorityRuleTakesItThoughTheOtherWasWrittenFirstAndSortsBeforeItById()
    {
        var streams = new HeldStreams([Terrestrial(Listed)]);
        RuleMatcher matcher = Matcher(streams);
        Rule first = Written("keyword=hill", 40, 1, name: "written first", at: At.AddDays(-9));
        Rule louder = Written("keyword=hill", 50, 15, name: "louder", at: At.AddDays(-1));

        RuleMatchRun run = await matcher.AgainstAsync(
            [first, louder],
            Guide(Programme(Listed, 1, "hill walking")),
            Cancel);

        Assert.Equal("louder", Assert.Single(run.Matches).Rule.Name);
    }

    [Fact]
    public async Task TheRuleThatLostWouldHaveTakenItOnItsOwn()
    {
        var streams = new HeldStreams([Terrestrial(Listed)]);
        RuleMatcher matcher = Matcher(streams);
        Rule first = Written("keyword=hill", 40, 1, name: "written first", at: At.AddDays(-9));

        RuleMatchRun run = await matcher.AgainstAsync(
            [first],
            Guide(Programme(Listed, 1, "hill walking")),
            Cancel);

        Assert.Equal("written first", Assert.Single(run.Matches).Rule.Name);
    }

    [Fact]
    public async Task RulesOfEqualWeightGoToTheOneWrittenEarlier()
    {
        var streams = new HeldStreams([Terrestrial(Listed)]);
        RuleMatcher matcher = Matcher(streams);
        Rule earlier = Written("keyword=hill", 30, 15, name: "earlier", at: At.AddDays(-9));
        Rule later = Written("keyword=hill", 30, 1, name: "later", at: At.AddDays(-2));

        RuleMatchRun run = await matcher.AgainstAsync(
            [later, earlier],
            Guide(Programme(Listed, 1, "hill walking")),
            Cancel);

        Assert.Equal("earlier", Assert.Single(run.Matches).Rule.Name);
    }

    [Fact]
    public async Task RulesWrittenAtTheSameMomentGoToTheSmallerIdentifier()
    {
        var streams = new HeldStreams([Terrestrial(Listed)]);
        RuleMatcher matcher = Matcher(streams);
        Rule smaller = Written("keyword=hill", 30, 1, name: "smaller", at: At.AddDays(-9));
        Rule larger = Written("keyword=hill", 30, 15, name: "larger", at: At.AddDays(-9));

        RuleMatchRun run = await matcher.AgainstAsync(
            [larger, smaller],
            Guide(Programme(Listed, 1, "hill walking")),
            Cancel);

        Assert.Equal("smaller", Assert.Single(run.Matches).Rule.Name);
    }

    [Fact]
    public async Task AQueryThatCannotBeReadTurnsItsRuleOffAndSaysSo()
    {
        var streams = new HeldStreams([Terrestrial(Listed)]);
        RuleMatcher matcher = Matcher(streams);
        Rule broken = Written("keyword=h", 10, 1, name: "one letter");

        RuleMatchRun run = await matcher.AgainstAsync(
            [broken],
            Guide(Programme(Listed, 1, "hill walking")),
            Cancel);

        Assert.Equal("one letter", Assert.Single(run.TurnedOff).Name);
        Assert.False(broken.Enabled);
        Assert.Empty(run.Matches);
    }

    [Fact]
    public async Task AValueOutOfItsRangeIsAQueryThatCannotBeRead()
    {
        var streams = new HeldStreams([Terrestrial(Listed)]);
        RuleMatcher matcher = Matcher(streams);
        Rule broken = Written("keyword=hill&genre=99", 10, 1, name: "out of range");

        RuleMatchRun run = await matcher.AgainstAsync(
            [broken],
            Guide(Programme(Listed, 1, "hill walking")),
            Cancel);

        Assert.Equal("out of range", Assert.Single(run.TurnedOff).Name);
        Assert.False(broken.Enabled);
    }

    [Fact]
    public async Task AQueryThatNarrowsNothingIsAQueryThatCannotBeRead()
    {
        var streams = new HeldStreams([Terrestrial(Listed)]);
        RuleMatcher matcher = Matcher(streams);
        Rule broken = Written("sort=Name&page=2&perPage=10", 10, 1, name: "narrows nothing");

        RuleMatchRun run = await matcher.AgainstAsync(
            [broken],
            Guide(Programme(Listed, 1, "hill walking")),
            Cancel);

        Assert.Equal("narrows nothing", Assert.Single(run.TurnedOff).Name);
        Assert.False(broken.Enabled);
    }

    [Fact]
    public async Task OneRuleThatCannotBeReadDoesNotSilenceTheRest()
    {
        var streams = new HeldStreams([Terrestrial(Listed)]);
        RuleMatcher matcher = Matcher(streams);
        Rule broken = Written("keyword=h", 90, 1, name: "loudest and broken");
        Rule sound = Written("keyword=hill", 10, 15, name: "quiet and sound");

        RuleMatchRun run = await matcher.AgainstAsync(
            [broken, sound],
            Guide(Programme(Listed, 1, "hill walking"), Programme(Listed, 2, "hillside")),
            Cancel);

        Assert.Equal("loudest and broken", Assert.Single(run.TurnedOff).Name);
        Assert.Equal(["hill walking", "hillside"], Named(run));
        Assert.All(run.Matches, match => Assert.Equal("quiet and sound", match.Rule.Name));
    }

    [Fact]
    public async Task HowManyFitOnAPageDoesNotDecideHowManyAreTaken()
    {
        var streams = new HeldStreams([Terrestrial(Listed)]);
        RuleMatcher matcher = Matcher(streams);

        RuleMatchRun run = await matcher.AgainstAsync(
            [Written("keyword=hill&perPage=1&page=3&sort=Name&descending=true", 10, 1)],
            Guide(
                Programme(Listed, 1, "hill walking"),
                Programme(Listed, 2, "the hill at dawn"),
                Programme(Listed, 3, "hillside")),
            Cancel);

        Assert.Equal(3, run.Matches.Count);
    }

    [Fact]
    public async Task WhatABroadcastTypeCarriesIsWorkedOutAgainOnEveryRun()
    {
        var streams = new HeldStreams([Terrestrial(Listed)]);
        RuleMatcher matcher = Matcher(streams);
        Rule rule = Written("keyword=hill&type=IsdbT", 10, 1);
        IReadOnlyList<ProgrammeMatch> guide = Guide(
            Programme(Listed, 1, "hill walking"),
            Programme(Beside, 2, "hillside"));

        RuleMatchRun before = await matcher.AgainstAsync([rule], guide, Cancel);

        streams.Carried.Clear();
        streams.Carried.Add(Terrestrial(Listed, Beside));

        RuleMatchRun after = await matcher.AgainstAsync([rule], guide, Cancel);

        Assert.Equal(["hill walking"], Named(before));
        Assert.Equal(["hill walking", "hillside"], Named(after));
    }

    [Fact]
    public async Task WhatTheGuideDoesNotListIsNotTaken()
    {
        var streams = new HeldStreams([Terrestrial(Listed, Unlisted)]);
        var catalogue = new HeldServices();
        catalogue.Services.Add(Service(Listed, ServiceCategory.Television));
        catalogue.Services.Add(Service(Unlisted, ServiceCategory.OneSeg));
        var matcher = new RuleMatcher(new ProgrammeSearchScope(streams, catalogue), new FixedClock(At));

        RuleMatchRun run = await matcher.AgainstAsync(
            [Written("keyword=hill", 10, 1)],
            Guide(Programme(Listed, 1, "hill walking"), Programme(Unlisted, 2, "hillside")),
            Cancel);

        Assert.Equal(["hill walking"], Named(run));
    }

    [Fact]
    public async Task TwoChannelsCarryingTheSameEventNumberAreTwoProgrammes()
    {
        var streams = new HeldStreams([Terrestrial(Listed, Beside)]);
        RuleMatcher matcher = Matcher(streams);

        RuleMatchRun run = await matcher.AgainstAsync(
            [Written("keyword=hill", 10, 1)],
            Guide(Programme(Listed, 7, "hill walking"), Programme(Beside, 7, "hillside")),
            Cancel);

        Assert.Equal(["hill walking", "hillside"], Named(run));
    }

    [Fact]
    public async Task AnEventNumberUsedAgainLaterIsAnotherProgramme()
    {
        var streams = new HeldStreams([Terrestrial(Listed)]);
        RuleMatcher matcher = Matcher(streams);

        RuleMatchRun run = await matcher.AgainstAsync(
            [Written("keyword=hill", 10, 1)],
            Guide(
                Broadcast(Listed, 7, "hill walking", [], false, At.AddHours(1)),
                Broadcast(Listed, 7, "hillside", [], false, At.AddDays(7))),
            Cancel);

        Assert.Equal(["hill walking", "hillside"], Named(run));
    }

    [Fact]
    public async Task AnEventThatOnlyShadowsAnotherIsNotTaken()
    {
        var streams = new HeldStreams([Terrestrial(Listed)]);
        RuleMatcher matcher = Matcher(streams);

        RuleMatchRun run = await matcher.AgainstAsync(
            [Written("keyword=hill", 10, 1)],
            Guide(Programme(Listed, 1, "hill walking"), Shadowing(Listed, 2, "hillside")),
            Cancel);

        Assert.Equal(["hill walking"], Named(run));
    }

    private static string[] Named(RuleMatchRun run)
        => [.. run.Matches.Select(match => match.Programme.Name).Order(StringComparer.Ordinal)];

    private static RuleMatcher Matcher(HeldStreams streams)
        => new(new ProgrammeSearchScope(streams, new HeldServices()), new FixedClock(At));

    private static IReadOnlyList<ProgrammeMatch> Guide(params Programme[] programmes)
        => ProgrammeSearchMatching.Layered(programmes, []);

    private static Rule Written(
        string query,
        int priority,
        int identifier,
        string name = "a rule",
        bool enabled = true,
        DateTime? at = null)
        => Rule.Draft(
            new RuleId(new Guid($"{identifier:x8}-0000-0000-0000-000000000000")),
            name,
            new RuleQuery(query),
            new Priority(priority),
            enabled,
            Margin.None,
            Margin.None,
            at ?? At.AddDays(-30));

    private static Programme Programme(int service, int carried, string name)
        => Broadcast(service, carried, name, [], false);

    private static Programme Filed(int service, int carried, string name, int genre)
        => Broadcast(service, carried, name, [new ProgrammeGenre(genre, 0)], false);

    private static Programme Shadowing(int service, int carried, string name)
        => Broadcast(service, carried, name, [], true);

    private static Programme Broadcast(
        int service,
        int carried,
        string name,
        IReadOnlyList<ProgrammeGenre> genres,
        bool shadow,
        DateTime? began = null)
        => Domain.Programmes.Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(Network), new ServiceId(service), new EventId(carried)),
                new TransportStreamId(32_736),
                began ?? At.AddHours(1),
                (began ?? At.AddHours(1)).AddHours(1),
                name,
                "a summary",
                shadow)
            {
                Genres = genres,
            },
            At);

    private static BroadcastService Service(int service, ServiceCategory category)
        => BroadcastService.Discover(
            new NetworkId(Network),
            new ServiceId(service),
            "a service",
            category,
            At);

    private static BroadcastStream Terrestrial(params int[] services)
        => new(
            new NetworkId(Network),
            new TransportStreamId(32_736),
            TuningParameters.Terrestrial(22),
            [.. services.Select(service => new ServiceId(service))]);
}
