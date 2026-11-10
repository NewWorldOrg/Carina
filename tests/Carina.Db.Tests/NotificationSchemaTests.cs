using Carina.Domain.Notifications;
using Carina.Infrastructure.Persistence.Configurations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class NotificationSchemaTests(MigratedScratchDatabase database) : IClassFixture<MigratedScratchDatabase>
{
    private const string At = "timestamptz '2026-11-09 12:00:00+00'";

    private const string Later = "timestamptz '2026-11-09 12:00:30+00'";

    private const string Event = NoticeConfiguration.TableName;

    private const string Destination = NotificationDestinationConfiguration.TableName;

    private const string Rule = NotificationRuleConfiguration.TableName;

    private const string Delivery = NotificationDeliveryConfiguration.TableName;

    private const string Settings = NotificationSettingsConfiguration.TableName;

    private const string Queued = $"'Queued', 0, {Later}, NULL, NULL";

    public static TheoryData<string> Kinds => Named(Enum.GetNames<NoticeKind>());

    public static TheoryData<string> Tones => Named(Enum.GetNames<NoticeTone>());

    public static TheoryData<string> Targets => Named(Enum.GetNames<WebhookTarget>());

    public static TheoryData<string> Failures => Named(Enum.GetNames<NoticeFailure>());

    [Theory(DisplayName = "the migrated notification tables hold these columns and no others")]
    [InlineData(Event, "body id kind link occurred_at source_id taken_at title tone")]
    [InlineData(Destination, "changed_at created_at enabled id last_failure last_result_at name sealed_url target url_tail")]
    [InlineData(Rule, "changed_at created_at destinations enabled events id name")]
    [InlineData(Delivery, "attempts destination_id event_id id last_failure next_attempt_at queued_at sent_at state")]
    [InlineData(Settings, "changed_at id notify_lead_minutes")]
    public async Task TheMigratedNotificationTablesHoldTheseColumns(string table, string columns)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            columns,
            await ScalarAsync(connection, $"SELECT string_agg(column_name::text, ' ' ORDER BY column_name) FROM information_schema.columns WHERE table_name = '{table}'"));
    }

    [Fact(DisplayName = "BR-NA-003: the destination keeps its address as sealed bytes and four closing characters, in no column of text that could hold the address")]
    public async Task TheDestinationKeepsItsAddressAsSealedBytes()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            "sealed_url:bytea url_tail:character varying(4)",
            await ScalarAsync(
                connection,
                $"""
                SELECT string_agg(attname || ':' || format_type(atttypid, atttypmod), ' ' ORDER BY attname)
                FROM pg_attribute
                WHERE attrelid = '{Destination}'::regclass AND attname LIKE '%url%'
                """));
    }

    [Theory(DisplayName = "BR-NT-004: an event is kept under every kind the domain names")]
    [MemberData(nameof(Kinds))]
    public async Task AnEventIsKeptUnderEveryKindTheDomainNames(string kind)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await RunAsync(connection, AnEvent(Guid.NewGuid(), kind, Guid.NewGuid()));
    }

    [Theory(DisplayName = "BR-ND-007: an event is kept under every tone the domain names")]
    [MemberData(nameof(Tones))]
    public async Task AnEventIsKeptUnderEveryToneTheDomainNames(string tone)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await RunAsync(connection, AnEvent(Guid.NewGuid(), nameof(NoticeKind.RecordingStarted), Guid.NewGuid(), tone));
    }

    [Theory(DisplayName = "an event of a kind or a tone outside the vocabulary, with no words, or with a link that is not a web address is refused")]
    [InlineData("'Unheard', 'Good', 'A title', 'A body', NULL", NoticeConfiguration.KindCheck)]
    [InlineData("'RecordingStarted', 'Loud', 'A title', 'A body', NULL", NoticeConfiguration.ToneCheck)]
    [InlineData("'RecordingStarted', 'Good', ' ', 'A body', NULL", NoticeConfiguration.WordsCheck)]
    [InlineData("'RecordingStarted', 'Good', 'A title', '', NULL", NoticeConfiguration.WordsCheck)]
    [InlineData("'RecordingStarted', 'Good', 'A title', 'A body', 'javascript:alert(1)'", NoticeConfiguration.WordsCheck)]
    public async Task AnEventOutsideWhatTheLedgerHoldsIsRefused(string values, string refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => RunAsync(
            connection,
            $"INSERT INTO {Event} (id, source_id, occurred_at, taken_at, kind, tone, title, body, link) VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', {At}, {At}, {values})"));

        Assert.Equal((PostgresErrorCodes.CheckViolation, refusedBy), (refused.SqlState, refused.ConstraintName));
    }

    [Fact(DisplayName = "BR-ND-004: one kind of event is kept once for one source row, and another kind of the same row beside it")]
    public async Task OneKindOfEventIsKeptOnceForOneSourceRow()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid source = Guid.NewGuid();
        await RunAsync(connection, AnEvent(Guid.NewGuid(), nameof(NoticeKind.RecordingStarted), source));
        await RunAsync(connection, AnEvent(Guid.NewGuid(), nameof(NoticeKind.RecordingCompleted), source));

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => RunAsync(connection, AnEvent(Guid.NewGuid(), nameof(NoticeKind.RecordingStarted), source)));

        Assert.Equal(
            (PostgresErrorCodes.UniqueViolation, NoticeConfiguration.SourceIndexName),
            (refused.SqlState, refused.ConstraintName));
    }

    [Fact(DisplayName = "BR-ND-010: one event is delivered once to one destination")]
    public async Task OneEventIsDeliveredOnceToOneDestination()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        (Guid notice, Guid destination) = await AnEventAndADestinationAsync(connection);
        await RunAsync(connection, ADelivery(notice, destination, Queued));

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => RunAsync(connection, ADelivery(notice, destination, Queued)));

        Assert.Equal(
            (PostgresErrorCodes.UniqueViolation, NotificationDeliveryConfiguration.OncePerDestinationIndexName),
            (refused.SqlState, refused.ConstraintName));
    }

    [Theory(DisplayName = "BR-NS-001: a delivery stands where its own columns put it")]
    [InlineData($"'Queued', 0, {Later}, NULL, NULL", null)]
    [InlineData($"'Failed', 1, {Later}, NULL, 'TimedOut'", null)]
    [InlineData($"'Failed', 3, {Later}, NULL, 'RateLimited'", null)]
    [InlineData($"'Sent', 1, NULL, {Later}, NULL", null)]
    [InlineData($"'GivenUp', 0, NULL, NULL, 'DestinationDisabled'", null)]
    [InlineData($"'GivenUp', 4, NULL, NULL, 'ServerFailed'", null)]
    [InlineData($"'Waiting', 0, {Later}, NULL, NULL", NotificationDeliveryConfiguration.StateCheck)]
    [InlineData($"'Queued', 1, {Later}, NULL, NULL", NotificationDeliveryConfiguration.StandingCheck)]
    [InlineData("'Queued', 0, NULL, NULL, NULL", NotificationDeliveryConfiguration.StandingCheck)]
    [InlineData($"'Failed', 0, {Later}, NULL, 'TimedOut'", NotificationDeliveryConfiguration.StandingCheck)]
    [InlineData($"'Failed', 1, {Later}, NULL, NULL", NotificationDeliveryConfiguration.StandingCheck)]
    [InlineData("'Failed', 1, NULL, NULL, 'TimedOut'", NotificationDeliveryConfiguration.StandingCheck)]
    [InlineData("'Sent', 1, NULL, NULL, NULL", NotificationDeliveryConfiguration.StandingCheck)]
    [InlineData($"'Sent', 0, NULL, {Later}, NULL", NotificationDeliveryConfiguration.StandingCheck)]
    [InlineData($"'Sent', 1, NULL, {Later}, 'TimedOut'", NotificationDeliveryConfiguration.StandingCheck)]
    [InlineData("'GivenUp', 1, NULL, NULL, NULL", NotificationDeliveryConfiguration.StandingCheck)]
    [InlineData($"'GivenUp', 1, {Later}, NULL, 'Rejected'", NotificationDeliveryConfiguration.StandingCheck)]
    [InlineData("'GivenUp', 5, NULL, NULL, 'Rejected'", NotificationDeliveryConfiguration.AttemptsCheck)]
    [InlineData("'GivenUp', -1, NULL, NULL, 'Rejected'", NotificationDeliveryConfiguration.AttemptsCheck)]
    [InlineData("'GivenUp', 1, NULL, NULL, 'Lost'", NotificationDeliveryConfiguration.LastFailureCheck)]
    public async Task ADeliveryStandsWhereItsOwnColumnsPutIt(string standing, string? refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        (Guid notice, Guid destination) = await AnEventAndADestinationAsync(connection);

        await HoldsOrIsRefusedAsync(connection, ADelivery(notice, destination, standing), refusedBy);
    }

    [Theory(DisplayName = "BR-NS-001: a delivery is kept under every failure the domain names")]
    [MemberData(nameof(Failures))]
    public async Task ADeliveryIsKeptUnderEveryFailureTheDomainNames(string failure)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        (Guid notice, Guid destination) = await AnEventAndADestinationAsync(connection);

        await RunAsync(connection, ADelivery(notice, destination, $"'GivenUp', 1, NULL, NULL, '{failure}'"));
    }

    [Fact(DisplayName = "BR-NS-001: removing a destination takes its deliveries with it and leaves the event")]
    public async Task RemovingADestinationTakesItsDeliveriesWithIt()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        (Guid notice, Guid destination) = await AnEventAndADestinationAsync(connection);
        await RunAsync(connection, ADelivery(notice, destination, Queued));

        await RunAsync(connection, $"DELETE FROM {Destination} WHERE id = '{destination}'");

        Assert.Equal("0", await ScalarAsync(connection, $"SELECT count(*) FROM {Delivery} WHERE event_id = '{notice}'"));
        Assert.Equal("1", await ScalarAsync(connection, $"SELECT count(*) FROM {Event} WHERE id = '{notice}'"));
    }

    [Fact(DisplayName = "removing an event takes its deliveries with it and leaves the destination")]
    public async Task RemovingAnEventTakesItsDeliveriesWithIt()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        (Guid notice, Guid destination) = await AnEventAndADestinationAsync(connection);
        await RunAsync(connection, ADelivery(notice, destination, Queued));

        await RunAsync(connection, $"DELETE FROM {Event} WHERE id = '{notice}'");

        Assert.Equal("0", await ScalarAsync(connection, $"SELECT count(*) FROM {Delivery} WHERE destination_id = '{destination}'"));
        Assert.Equal("1", await ScalarAsync(connection, $"SELECT count(*) FROM {Destination} WHERE id = '{destination}'"));
    }

    [Fact(DisplayName = "a delivery to a destination or of an event that is not held is refused")]
    public async Task ADeliveryOfWhatIsNotHeldIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        (Guid notice, Guid destination) = await AnEventAndADestinationAsync(connection);

        PostgresException nowhere = await Assert.ThrowsAsync<PostgresException>(
            () => RunAsync(connection, ADelivery(notice, Guid.NewGuid(), Queued)));
        PostgresException ofNothing = await Assert.ThrowsAsync<PostgresException>(
            () => RunAsync(connection, ADelivery(Guid.NewGuid(), destination, Queued)));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, nowhere.SqlState);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ofNothing.SqlState);
    }

    [Theory(DisplayName = "a destination is kept under every target the domain names")]
    [MemberData(nameof(Targets))]
    public async Task ADestinationIsKeptUnderEveryTargetTheDomainNames(string target)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await RunAsync(connection, ADestination(Guid.NewGuid(), $"'A destination', '{target}', '\\x01'::bytea, 'wxyz', NULL, NULL"));
    }

    [Theory(DisplayName = "BR-NT-003: a destination holds a trimmed name, a known target, sealed bytes, four closing characters and a failure only beside its moment")]
    [InlineData("'A destination', 'Slack', '\\x01'::bytea, 'wxyz', NULL, NULL", null)]
    [InlineData($"'A destination', 'Discord', '\\x01'::bytea, 'wxyz', {Later}, 'Rejected'", null)]
    [InlineData($"'A destination', 'Discord', '\\x01'::bytea, 'wxyz', {Later}, NULL", null)]
    [InlineData("'', 'Slack', '\\x01'::bytea, 'wxyz', NULL, NULL", NotificationDestinationConfiguration.NameCheck)]
    [InlineData("' padded', 'Slack', '\\x01'::bytea, 'wxyz', NULL, NULL", NotificationDestinationConfiguration.NameCheck)]
    [InlineData("'A destination', 'Pager', '\\x01'::bytea, 'wxyz', NULL, NULL", NotificationDestinationConfiguration.TargetCheck)]
    [InlineData("'A destination', 'Slack', ''::bytea, 'wxyz', NULL, NULL", NotificationDestinationConfiguration.SealedUrlCheck)]
    [InlineData("'A destination', 'Slack', '\\x01'::bytea, 'xyz', NULL, NULL", NotificationDestinationConfiguration.UrlTailCheck)]
    [InlineData("'A destination', 'Slack', '\\x01'::bytea, 'wxyz', NULL, 'Rejected'", NotificationDestinationConfiguration.LastFailureCheck)]
    [InlineData($"'A destination', 'Slack', '\\x01'::bytea, 'wxyz', {Later}, 'Lost'", NotificationDestinationConfiguration.LastFailureCheck)]
    public async Task ADestinationHoldsWhatItCanHold(string values, string? refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await HoldsOrIsRefusedAsync(connection, ADestination(Guid.NewGuid(), values), refusedBy);
    }

    [Theory(DisplayName = "BR-NT-004: a rule set holds events out of the vocabulary alone, none of them if it likes, and at least one destination")]
    [InlineData("'A rule set', '[]', '[\"8f2f6f0e-5d0b-4c57-9d3e-3f2a1b0c9d8e\"]'", null)]
    [InlineData("'A rule set', '[\"RecordingFailed\", \"EncodeFailed\"]', '[\"8f2f6f0e-5d0b-4c57-9d3e-3f2a1b0c9d8e\"]'", null)]
    [InlineData("'A rule set', '[\"RecordingFailed\", \"Unheard\"]', '[\"8f2f6f0e-5d0b-4c57-9d3e-3f2a1b0c9d8e\"]'", NotificationRuleConfiguration.EventsCheck)]
    [InlineData("'A rule set', '\"RecordingFailed\"', '[\"8f2f6f0e-5d0b-4c57-9d3e-3f2a1b0c9d8e\"]'", NotificationRuleConfiguration.EventsCheck)]
    [InlineData("'A rule set', '{}', '[\"8f2f6f0e-5d0b-4c57-9d3e-3f2a1b0c9d8e\"]'", NotificationRuleConfiguration.EventsCheck)]
    [InlineData("'A rule set', '[]', '[]'", NotificationRuleConfiguration.DestinationsCheck)]
    [InlineData("'A rule set', '[]', '{}'", NotificationRuleConfiguration.DestinationsCheck)]
    [InlineData("' ', '[]', '[\"8f2f6f0e-5d0b-4c57-9d3e-3f2a1b0c9d8e\"]'", NotificationRuleConfiguration.NameCheck)]
    public async Task ARuleSetHoldsWhatItCanHold(string values, string? refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await HoldsOrIsRefusedAsync(
            connection,
            $"INSERT INTO {Rule} (id, enabled, created_at, changed_at, name, events, destinations) VALUES ('{Guid.NewGuid()}', TRUE, {At}, {At}, {values})",
            refusedBy);
    }

    [Theory(DisplayName = "BR-NT-004: a rule set holds every kind the domain names")]
    [MemberData(nameof(Kinds))]
    public async Task ARuleSetHoldsEveryKindTheDomainNames(string kind)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await RunAsync(
            connection,
            $"INSERT INTO {Rule} (id, enabled, created_at, changed_at, name, events, destinations) VALUES ('{Guid.NewGuid()}', TRUE, {At}, {At}, 'A rule set', '[\"{kind}\"]', '[\"{Guid.NewGuid()}\"]')");
    }

    [Theory(DisplayName = "BR-NT-005: the settings are one row whose lead is 0, 1, 3, 5 or 10 minutes")]
    [InlineData("1, 0", null)]
    [InlineData("1, 1", null)]
    [InlineData("1, 3", null)]
    [InlineData("1, 5", null)]
    [InlineData("1, 10", null)]
    [InlineData("1, 2", NotificationSettingsConfiguration.LeadCheck)]
    [InlineData("1, 15", NotificationSettingsConfiguration.LeadCheck)]
    [InlineData("1, -1", NotificationSettingsConfiguration.LeadCheck)]
    [InlineData("2, 3", NotificationSettingsConfiguration.SingleRowCheck)]
    public async Task TheSettingsAreOneRowOfALeadOnOffer(string values, string? refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await RunAsync(connection, $"DELETE FROM {Settings}");

        await HoldsOrIsRefusedAsync(
            connection,
            $"INSERT INTO {Settings} (id, notify_lead_minutes, changed_at) VALUES ({values}, {At})",
            refusedBy);
    }

    private static string AnEvent(Guid id, string kind, Guid source, string tone = nameof(NoticeTone.Informative))
        => $"INSERT INTO {Event} (id, kind, source_id, occurred_at, tone, title, body, link, taken_at) VALUES ('{id}', '{kind}', '{source}', {At}, '{tone}', 'A title', 'A body', NULL, {At})";

    private static string ADestination(Guid id, string values)
        => $"INSERT INTO {Destination} (id, enabled, created_at, changed_at, name, target, sealed_url, url_tail, last_result_at, last_failure) VALUES ('{id}', TRUE, {At}, {At}, {values})";

    private static string ADelivery(Guid notice, Guid destination, string standing)
        => $"INSERT INTO {Delivery} (id, event_id, destination_id, queued_at, state, attempts, next_attempt_at, sent_at, last_failure) VALUES ('{Guid.NewGuid()}', '{notice}', '{destination}', {At}, {standing})";

    private static async Task<(Guid Notice, Guid Destination)> AnEventAndADestinationAsync(NpgsqlConnection connection)
    {
        Guid notice = Guid.NewGuid();
        Guid destination = Guid.NewGuid();
        await RunAsync(connection, AnEvent(notice, nameof(NoticeKind.RecordingStarted), Guid.NewGuid()));
        await RunAsync(connection, ADestination(destination, "'A destination', 'Slack', '\\x01'::bytea, 'wxyz', NULL, NULL"));

        return (notice, destination);
    }

    private static async Task HoldsOrIsRefusedAsync(NpgsqlConnection connection, string inserting, string? refusedBy)
    {
        if (refusedBy is null)
        {
            await RunAsync(connection, inserting);

            return;
        }

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => RunAsync(connection, inserting));

        Assert.Equal((PostgresErrorCodes.CheckViolation, refusedBy), (refused.SqlState, refused.ConstraintName));
    }

    private static TheoryData<string> Named(IEnumerable<string> names)
    {
        var data = new TheoryData<string>();

        foreach (string name in names)
        {
            data.Add(name);
        }

        return data;
    }

    private static async Task RunAsync(NpgsqlConnection connection, string sql)
    {
        await using var running = new NpgsqlCommand(sql, connection);

        await running.ExecuteNonQueryAsync();
    }

    private static async Task<string> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var reading = new NpgsqlCommand(sql, connection);

        return Convert.ToString(await reading.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
