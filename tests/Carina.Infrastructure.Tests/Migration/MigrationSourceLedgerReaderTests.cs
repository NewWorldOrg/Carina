using Carina.Domain.Migration;
using Carina.Infrastructure.Migration;

namespace Carina.Infrastructure.Tests.Migration;

public sealed class MigrationSourceLedgerReaderTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly StandInSourceDatabase source = new();

    [Fact]
    public async Task ItTellsTheSourceItIsOnlyReadingBeforeItAsksForAnything()
    {
        await Reader().ReadAsync(Cancel);

        Assert.Equal("START TRANSACTION READ ONLY", source.Statements[0]);
        Assert.Equal("COMMIT", source.Statements[^1]);
    }

    [Fact]
    public async Task EverythingBetweenIsASelectAndNothingElse()
    {
        await Reader().ReadAsync(Cancel);

        Assert.Equal(MigrationSourceLedgerReader.Populations.Count, source.Statements.Count - 2);
        Assert.All(
            source.Statements[1..^1],
            statement => Assert.StartsWith("SELECT ", statement, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ItReadsTheFivePopulationsAndNothingBeside()
    {
        await Reader().ReadAsync(Cancel);

        Assert.Equal(
            ["FROM channel", "FROM recorded", "FROM reserve", "FROM rule", "FROM video_file"],
            source.Statements
                .SelectMany(statement => MigrationSourceLedgerReader.Populations
                    .Where(table => statement.Contains($"FROM {table}", StringComparison.Ordinal))
                    .Select(table => $"FROM {table}"))
                .Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("program")]
    [InlineData("recorded_history")]
    [InlineData("recorded_tag")]
    [InlineData("thumbnail")]
    [InlineData("drop_log_file")]
    [InlineData("migrations")]
    [InlineData("information_schema")]
    public async Task WhatIsNotBeingCarriedIsNotEvenLookedAt(string table)
    {
        await Reader().ReadAsync(Cancel);

        Assert.Equal(MigrationSourceLedgerReader.Populations.Count, source.Statements.Count - 2);
        Assert.All(
            source.Statements,
            statement => Assert.DoesNotContain($"FROM {table}", statement, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheConnectionIsOpenedOnceAndGivenBack()
    {
        await Reader().ReadAsync(Cancel);

        Assert.Equal(1, source.Opened);
        Assert.Equal(1, source.Closed);
    }

    [Fact]
    public async Task ASourceThatCannotBeReachedIsSaidToBeUnreadable()
    {
        source.WhenOpening = new InvalidOperationException("no");

        await Assert.ThrowsAsync<MigrationSourceUnreadableException>(() => Reader().ReadAsync(Cancel));
    }

    [Fact]
    public async Task ARecordingCarriesTheTimesTheSourceKeptInMillisecondsSinceTheEpoch()
    {
        source.Holds("recorded", Recorded(7, channel: 3273601024, programme: 327360102400512));
        source.Holds("channel", Channel(3273601024, 32736, 1024, "GR"));

        SourceRecording recording = (await Reader().ReadAsync(Cancel)).Recordings.Single();

        Assert.Equal(new DateTime(2025, 9, 1, 12, 0, 0, DateTimeKind.Utc), recording.StartAt);
        Assert.Equal(new DateTime(2025, 9, 1, 13, 0, 0, DateTimeKind.Utc), recording.EndAt);
        Assert.Equal(7, recording.Id);
    }

    [Fact]
    public async Task ARecordingIsIdentifiedByTheServiceItsChannelRowNames()
    {
        source.Holds("recorded", Recorded(7, channel: 3273601024, programme: 327360102400512));
        source.Holds("channel", Channel(3273601024, 32736, 1024, "GR"));

        SourceRecording recording = (await Reader().ReadAsync(Cancel)).Recordings.Single();

        Assert.Equal(ServiceKey.Of(32736, 1024), recording.Service);
        Assert.Equal(512, recording.Programme?.Value);
    }

    [Fact]
    public async Task ARecordingWhoseChannelTheSourceNoLongerDefinesIsIdentifiedByNothing()
    {
        source.Holds("recorded", Recorded(7, channel: 3273601024, programme: 327360102400512));

        SourceRecording recording = (await Reader().ReadAsync(Cancel)).Recordings.Single();

        Assert.Null(recording.Service);
    }

    [Fact]
    public async Task AProgrammeIdentifierOutsideWhatAnEventCanBeIsNoProgrammeAtAll()
    {
        source.Holds("recorded", Recorded(7, channel: 3273601024, programme: 327360102465535));
        source.Holds("channel", Channel(3273601024, 32736, 1024, "GR"));

        SourceRecording recording = (await Reader().ReadAsync(Cancel)).Recordings.Single();

        Assert.Null(recording.Programme);
        Assert.NotNull(recording.Service);
    }

    [Fact]
    public async Task ARecordingTheSourceNeverTiedToAProgrammeIsReadWithoutOne()
    {
        source.Holds("recorded", Recorded(7, channel: 3273601024, programme: null));
        source.Holds("channel", Channel(3273601024, 32736, 1024, "GR"));

        Assert.Null((await Reader().ReadAsync(Cancel)).Recordings.Single().Programme);
    }

    [Fact]
    public async Task ARecordingThatEndsBeforeItStartsStopsTheReadRatherThanBeingBent()
    {
        source.Holds("recorded", Recorded(7, channel: 3273601024, programme: null, endsAt: 1_756_724_400_000));

        await Assert.ThrowsAsync<MigrationSourceUnreadableException>(() => Reader().ReadAsync(Cancel));
    }

    [Fact]
    public async Task AFileOfTheSourceIsReadAsWhatTheSourceCallsIt()
    {
        source.Holds(
            "video_file",
            VideoFile(7, "one.m2ts", "ts", 4096),
            VideoFile(7, "one.mp4", "encoded", 2048));

        IReadOnlyList<SourceRecordingFile> files = (await Reader().ReadAsync(Cancel)).RecordingFiles;

        Assert.Equal(SourceFileKind.AsBroadcast, files[0].Kind);
        Assert.Equal(SourceFileKind.Encoded, files[1].Kind);
        Assert.Equal(4096, files[0].SizeRecorded);
    }

    [Fact]
    public async Task AFileOfAKindTheSourceHasNoNameForStopsTheRead()
    {
        source.Holds("video_file", VideoFile(7, "one.m2ts", "something else", 4096));

        await Assert.ThrowsAsync<MigrationSourceUnreadableException>(() => Reader().ReadAsync(Cancel));
    }

    [Fact]
    public async Task ASourceKeepingItsRecordingsUnderMoreThanOneRootStopsTheReadBecauseARunIsGivenOne()
    {
        source.Holds(
            "video_file",
            VideoFile(7, "one.m2ts", "ts", 4096),
            VideoFile(8, "two.m2ts", "ts", 4096, parent: "elsewhere"));

        await Assert.ThrowsAsync<MigrationSourceUnreadableException>(() => Reader().ReadAsync(Cancel));
    }

    [Fact]
    public async Task ARuleIsReadByWhatItAsksForAndByWhetherItIsOn()
    {
        source.Holds("rule", Rule(3, enabled: false));

        SourceRule rule = (await Reader().ReadAsync(Cancel)).Rules.Single();

        Assert.Equal(3, rule.Id);
        Assert.False(rule.Enabled);
        Assert.Equal(SourceRuleReach.Plain, rule.Reach);
        Assert.Empty(rule.Services);
        Assert.Equal("something", rule.Terms.Keyword);
        Assert.Equal(SourceRuleFields.Title | SourceRuleFields.Summary, rule.Terms.Fields);
        Assert.Equal(SourceWeek.EveryDay, rule.Terms.Days);
    }

    [Fact]
    public async Task ARuleThatMatchesOnAPatternOrOnLetterCaseSaysSoEitherWay()
    {
        source.Holds(
            "rule",
            Rule(3, with: [("keyRegExp", 1L)]),
            Rule(4, with: [("ignoreKeyRegExp", 1L)]),
            Rule(5, with: [("keyCS", 1L)]),
            Rule(6, with: [("ignoreKeyCS", 1L)]));

        IReadOnlyList<SourceRule> rules = (await Reader().ReadAsync(Cancel)).Rules;

        Assert.Equal([true, true, false, false], rules.Select(rule => rule.Reach.UsesRegularExpression));
        Assert.Equal([false, false, true, true], rules.Select(rule => rule.Reach.CaseSensitive));
    }

    [Fact]
    public async Task ARuleThatOnlyNamesTheDaysOfTheWeekIsNotARuleAboutTheTimeOfDay()
    {
        source.Holds("rule", Rule(3, with: [("times", "[{\"week\":127}]")]));

        Assert.False((await Reader().ReadAsync(Cancel)).Rules.Single().Reach.RecordsAtATimeOfDay);
    }

    [Fact]
    public async Task ARuleThatNamesAnHourOfTheDayIsOneAboutTheTimeOfDay()
    {
        source.Holds("rule", Rule(3, with: [("times", "[{\"week\":127,\"start\":72000,\"range\":3600}]")]));

        Assert.True((await Reader().ReadAsync(Cancel)).Rules.Single().Reach.RecordsAtATimeOfDay);
    }

    [Fact]
    public async Task ARuleThatRecordsAClockSlotRatherThanAProgrammeIsOneAboutTheTimeOfDay()
    {
        source.Holds("rule", Rule(3, with: [("isTimeSpecification", 1L)]));

        Assert.True((await Reader().ReadAsync(Cancel)).Rules.Single().Reach.RecordsAtATimeOfDay);
    }

    [Fact]
    public async Task ARuleBoundByLengthOrByPeriodSaysWhichOfTheTwoItIs()
    {
        source.Holds(
            "rule",
            Rule(3, with: [("durationMin", 600L)]),
            Rule(4, with: [("durationMax", 7200L)]),
            Rule(5, with: [("searchPeriods", "[{\"startAt\":1,\"endAt\":2}]")]));

        IReadOnlyList<SourceRule> rules = (await Reader().ReadAsync(Cancel)).Rules;

        Assert.Equal([true, true, false], rules.Select(rule => rule.Reach.BoundsTheDuration));
        Assert.Equal([false, false, true], rules.Select(rule => rule.Reach.BoundsThePeriod));
    }

    [Fact]
    public async Task ARuleWithAPlaceOrAnEncodeSettingOfItsOwnSaysWhichOfTheTwoItIs()
    {
        source.Holds(
            "rule",
            Rule(3, with: [("directory", "somewhere")]),
            Rule(4, with: [("parentDirectoryName", "somewhere")]),
            Rule(5, with: [("mode2", "some setting")]));

        IReadOnlyList<SourceRule> rules = (await Reader().ReadAsync(Cancel)).Rules;

        Assert.Equal([true, true, false], rules.Select(rule => rule.Reach.NamesItsOwnDestination));
        Assert.Equal([false, false, true], rules.Select(rule => rule.Reach.NamesItsOwnEncodeSettings));
    }

    [Fact]
    public async Task ARuleTiedToChannelsCarriesThemAsTheServicesTheyStandFor()
    {
        source.Holds("rule", Rule(3, with: [("channelIds", "[3273601024,3273601025]")]));

        IReadOnlyList<ServiceKey> services = (await Reader().ReadAsync(Cancel)).Rules.Single().Services;

        Assert.Equal([ServiceKey.Of(32736, 1024), ServiceKey.Of(32736, 1025)], services);
    }

    [Fact]
    public async Task ARuleWhoseStoredConditionsCannotBeReadStopsTheRead()
    {
        source.Holds("rule", Rule(3, with: [("times", "not json at all")]));

        await Assert.ThrowsAsync<MigrationSourceUnreadableException>(() => Reader().ReadAsync(Cancel));
    }

    [Fact]
    public async Task AReservationSaysWhetherARuleMadeIt()
    {
        source.Holds("reserve", Reserve(4, rule: 3), Reserve(5, rule: null));

        IReadOnlyList<SourceReservation> reservations = (await Reader().ReadAsync(Cancel)).Reservations;

        Assert.Equal([true, false], reservations.Select(reservation => reservation.FromARule));
        Assert.Equal([4, 5], reservations.Select(reservation => reservation.Id));
    }

    [Fact]
    public async Task AChannelDefinitionIsReadAsTheKindOfBroadcastItSaysItIs()
    {
        source.Holds(
            "channel",
            Channel(1, 32736, 1024, "GR"),
            Channel(2, 4, 101, "BS"),
            Channel(3, 6, 296, "CS"),
            Channel(4, 1, 1, "SKY"));

        IReadOnlyList<SourceChannelDefinition> definitions = (await Reader().ReadAsync(Cancel)).ChannelDefinitions;

        Assert.Equal(
            [
                SourceBroadcastKind.Terrestrial,
                SourceBroadcastKind.BroadcastSatellite,
                SourceBroadcastKind.CommunicationSatellite,
                SourceBroadcastKind.Sky,
            ],
            definitions.Select(definition => definition.Kind));
    }

    [Fact]
    public async Task AChannelOfAKindTheSourceHasNoNameForStopsTheRead()
    {
        source.Holds("channel", Channel(1, 32736, 1024, "something else"));

        await Assert.ThrowsAsync<MigrationSourceUnreadableException>(() => Reader().ReadAsync(Cancel));
    }

    [Fact]
    public async Task AChannelDefinitionCarriesTheChannelTheSourceSystemWasReceivingItOn()
    {
        source.Holds("channel", Channel(1, 32736, 1024, "GR", "21"), Channel(2, 32736, 1025, "GR", "27"));

        Assert.Equal(
            ["21", "27"],
            (await Reader().ReadAsync(Cancel)).ChannelDefinitions
                .Select(definition => definition.PhysicalChannel));
    }

    [Fact]
    public async Task ARuleCarriesTheWordsItLooksForAndTheWordsItLeavesOut()
    {
        source.Holds("rule", Rule(3, with: [("keyword", "hill"), ("ignoreKeyword", "repeat")]));

        SourceRuleTerms terms = (await Reader().ReadAsync(Cancel)).Rules.Single().Terms;

        Assert.Equal("hill", terms.Keyword);
        Assert.Equal("repeat", terms.Excluded);
    }

    [Fact]
    public async Task ARuleCarriesWhichOfTheThreeFieldsItLooksIn()
    {
        source.Holds("rule", Rule(3, with: [("description", 0L), ("extended", 1L)]));

        SourceRuleTerms terms = (await Reader().ReadAsync(Cancel)).Rules.Single().Terms;

        Assert.Equal(SourceRuleFields.Title | SourceRuleFields.ExtendedBody, terms.Fields);
    }

    [Fact]
    public async Task ARuleCarriesTheKindsOfBroadcastItAsksFor()
    {
        source.Holds("rule", Rule(3, with: [("GR", 1L), ("CS", 1L)]));

        Assert.Equal(
            [SourceBroadcastKind.Terrestrial, SourceBroadcastKind.CommunicationSatellite],
            (await Reader().ReadAsync(Cancel)).Rules.Single().Terms.Kinds);
    }

    [Fact]
    public async Task ARuleCarriesTheGenreAndTheSubGenreItNarrowsTo()
    {
        source.Holds("rule", Rule(3, with: [("genres", "[{\"genre\":9,\"subGenre\":2}]")]));

        SourceRuleGenre genre = Assert.Single((await Reader().ReadAsync(Cancel)).Rules.Single().Terms.Genres);

        Assert.Equal(9, genre.Genre);
        Assert.Equal(2, genre.SubGenre);
    }

    [Fact]
    public async Task ARuleThatNamesOnlySomeDaysOfTheWeekCarriesWhichOnes()
    {
        source.Holds("rule", Rule(3, with: [("times", "[{\"week\":64}]")]));

        Assert.Equal(0b100_0000, (await Reader().ReadAsync(Cancel)).Rules.Single().Terms.Days);
    }

    [Fact]
    public async Task ARuleThatSaysNothingAboutTheDaysOfTheWeekAsksForEveryDay()
    {
        source.Holds("rule", Rule(3));

        Assert.Equal(SourceWeek.EveryDay, (await Reader().ReadAsync(Cancel)).Rules.Single().Terms.Days);
    }

    [Fact]
    public async Task TheRunSaysWhichSystemItReadWithoutSayingWhereItIs()
    {
        SourceLedger ledger = await Reader().ReadAsync(Cancel);

        Assert.Equal(MigrationSourceLedgerReader.Source, ledger.Name);
    }

    private static Dictionary<string, object?> Recorded(
        long id,
        long channel,
        long? programme,
        long startsAt = 1_756_728_000_000,
        long endsAt = 1_756_731_600_000)
        => new(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["name"] = "a programme",
            ["startAt"] = startsAt,
            ["endAt"] = endsAt,
            ["channelId"] = channel,
            ["programId"] = programme,
        };

    private static Dictionary<string, object?> Channel(
        long id,
        long network,
        long service,
        string kind,
        string physicalChannel = "21")
        => new(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["name"] = "a station",
            ["channelType"] = kind,
            ["networkId"] = network,
            ["serviceId"] = service,
            ["channel"] = physicalChannel,
        };

    private static Dictionary<string, object?> VideoFile(
        long recording,
        string path,
        string kind,
        long size,
        string parent = "recorded")
        => new(StringComparer.Ordinal)
        {
            ["recordedId"] = recording,
            ["parentDirectoryName"] = parent,
            ["filePath"] = path,
            ["type"] = kind,
            ["size"] = size,
        };

    private static Dictionary<string, object?> Reserve(long id, long? rule)
        => new(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["name"] = "a programme",
            ["halfWidthName"] = "a programme",
            ["ruleId"] = rule,
        };

    private static Dictionary<string, object?> Rule(
        long id,
        bool enabled = true,
        IReadOnlyList<(string Column, object? Value)>? with = null)
    {
        Dictionary<string, object?> row = new(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["keyword"] = "something",
            ["ignoreKeyword"] = null,
            ["enable"] = enabled ? 1L : 0L,
            ["channelIds"] = null,
            ["genres"] = null,
            ["name"] = 1L,
            ["description"] = 1L,
            ["extended"] = 0L,
            ["ignoreName"] = 1L,
            ["ignoreDescription"] = 1L,
            ["ignoreExtended"] = 0L,
            ["GR"] = 0L,
            ["BS"] = 0L,
            ["CS"] = 0L,
            ["SKY"] = 0L,
            ["keyCS"] = 0L,
            ["ignoreKeyCS"] = 0L,
            ["keyRegExp"] = 0L,
            ["ignoreKeyRegExp"] = 0L,
            ["isTimeSpecification"] = 0L,
            ["times"] = null,
            ["durationMin"] = null,
            ["durationMax"] = null,
            ["searchPeriods"] = null,
            ["parentDirectoryName"] = null,
            ["directory"] = null,
            ["mode1"] = null,
            ["mode2"] = null,
            ["mode3"] = null,
        };

        foreach ((string Column, object? Value) named in with ?? [])
        {
            row[named.Column] = named.Value;
        }

        return row;
    }

    private MigrationSourceLedgerReader Reader() => new(source);
}
