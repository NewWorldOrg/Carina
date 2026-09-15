using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationOutcomeSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string Index = "ux_reservation_outcome_reservation_kind";

    public static TheoryData<string> Kinds => Named<ReservationOutcomeKind>();

    public static TheoryData<string> Results => Named<RetryResult>();

    public static TheoryData<string> Reasons => Named<RetryGiveUp>();

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task EveryKindTheApplicationCanNameIsOneTheLedgerTakes(string kind)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, Guid.NewGuid(), kind);
    }

    [Fact]
    public async Task TheLedgerRefusesAClassTheApplicationCannotName()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                Guid.NewGuid(),
                nameof(ReservationOutcomeKind.Missed),
                faults: "'[\"Vanished\"]'::jsonb"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_reservation_outcome_faults", refusal.ConstraintName);
    }

    [Fact]
    public async Task ATuneFailureThatDoesNotNameItselfAsOneIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                Guid.NewGuid(),
                nameof(ReservationOutcomeKind.TuneFailure),
                faults: "'[\"TunerContended\"]'::jsonb"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_reservation_outcome_faults", refusal.ConstraintName);
    }

    [Fact]
    public async Task EveryClassTheRecorderCanNameIsOneTheLedgerTakes()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        string named = string.Join(", ", Enum.GetNames<RecordingFault>().Select(name => $"\"{name}\""));

        await Record(
            connection,
            Guid.NewGuid(),
            nameof(ReservationOutcomeKind.RecordingFailure),
            faults: $"'[{named}]'::jsonb");
    }

    [Fact]
    public async Task TheLedgerRefusesAKindTheApplicationCannotName()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, Guid.NewGuid(), "Vanished"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_reservation_outcome_kind", refusal.ConstraintName);
    }

    [Fact]
    public async Task OneReservationCarriesOneRowOfEachKindAndNoMore()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        var reservation = Guid.NewGuid();

        await Record(connection, reservation, nameof(ReservationOutcomeKind.Missed));
        await Record(connection, reservation, nameof(ReservationOutcomeKind.Competing));

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, reservation, nameof(ReservationOutcomeKind.Missed)));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refusal.SqlState);
        Assert.Equal(Index, refusal.ConstraintName);
    }

    [Fact]
    public async Task WhatKeepsOneRowOfEachKindIsAUniqueIndexOverTheReservationAndTheKind()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await using var asking = new NpgsqlCommand(
            """
            SELECT held.indisunique, array_agg(named.attname ORDER BY position.ord)
            FROM pg_index AS held
            JOIN pg_class AS entry ON entry.oid = held.indexrelid
            JOIN unnest(held.indkey) WITH ORDINALITY AS position(attnum, ord) ON true
            JOIN pg_attribute AS named
              ON named.attrelid = held.indrelid AND named.attnum = position.attnum
            WHERE entry.relname = $1
            GROUP BY held.indisunique
            """,
            connection);
        asking.Parameters.Add(new NpgsqlParameter { Value = Index });

        await using NpgsqlDataReader reading = await asking.ExecuteReaderAsync();

        Assert.True(await reading.ReadAsync(), $"the ledger carries an index named {Index}");
        Assert.True(reading.GetBoolean(0), "the index is unique");
        Assert.Equal(["reservation_id", "kind"], reading.GetFieldValue<string[]>(1));
        Assert.False(await reading.ReadAsync(), "the index is named once");
    }

    [Fact]
    public async Task OneReservationKeepsEveryRetryButGivesUpOnlyOnce()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        Guid reservation = Guid.NewGuid();

        await Record(connection, reservation, nameof(ReservationOutcomeKind.Retried));
        await Record(connection, reservation, nameof(ReservationOutcomeKind.Retried));
        await Record(connection, reservation, nameof(ReservationOutcomeKind.Retried));
        await Record(connection, reservation, nameof(ReservationOutcomeKind.GaveUpRetrying));

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(connection, reservation, nameof(ReservationOutcomeKind.GaveUpRetrying)));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refusal.SqlState);
        Assert.Equal(Index, refusal.ConstraintName);
        Assert.Equal(3L, await CountAsync(connection, reservation, nameof(ReservationOutcomeKind.Retried)));
    }

    [Theory]
    [MemberData(nameof(Results))]
    public async Task EveryResultARetryCanComeToIsOneTheLedgerTakes(string result)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, Guid.NewGuid(), nameof(ReservationOutcomeKind.Retried), retryResult: $"'{result}'");
    }

    [Theory]
    [MemberData(nameof(Reasons))]
    public async Task EveryReasonToGiveUpIsOneTheLedgerTakes(string reason)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(
            connection,
            Guid.NewGuid(),
            nameof(ReservationOutcomeKind.GaveUpRetrying),
            tuneFailure: reason == nameof(RetryGiveUp.NotTransient) ? "'StreamMismatch'" : "NULL",
            faults: reason == nameof(RetryGiveUp.NotTransient) ? "'[\"TuneFailed\"]'::jsonb" : null,
            gaveUpBecause: $"'{reason}'");
    }

    [Theory]
    [InlineData(nameof(ReservationOutcomeKind.Retried), "NULL", "NULL", "'[]'::jsonb")]
    [InlineData(nameof(ReservationOutcomeKind.Retried), "'Vanished'", "NULL", "'[]'::jsonb")]
    [InlineData(nameof(ReservationOutcomeKind.Missed), "'Started'", "NULL", "'[]'::jsonb")]
    [InlineData(nameof(ReservationOutcomeKind.Retried), "'Started'", "'NoLock'", "'[\"TuneFailed\"]'::jsonb")]
    [InlineData(nameof(ReservationOutcomeKind.Retried), "'Started'", "NULL", "'[\"TunerContended\"]'::jsonb")]
    public async Task ARetryThatDoesNotSayWhatCameOfItIsRefused(
        string kind,
        string retryResult,
        string tuneFailure,
        string faults)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                Guid.NewGuid(),
                kind,
                tuneFailure: tuneFailure,
                faults: faults,
                retryResult: retryResult));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_reservation_outcome_retry", refusal.ConstraintName);
    }

    [Theory]
    [InlineData(nameof(ReservationOutcomeKind.GaveUpRetrying), "NULL", "NULL")]
    [InlineData(nameof(ReservationOutcomeKind.GaveUpRetrying), "'Bored'", "NULL")]
    [InlineData(nameof(ReservationOutcomeKind.Missed), "'BroadcastOver'", "NULL")]
    [InlineData(nameof(ReservationOutcomeKind.GaveUpRetrying), "'NotTransient'", "NULL")]
    [InlineData(nameof(ReservationOutcomeKind.GaveUpRetrying), "'BroadcastOver'", "'NoLock'")]
    public async Task GivingUpThatDoesNotSayWhyIsRefused(string kind, string gaveUpBecause, string tuneFailure)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Record(
                connection,
                Guid.NewGuid(),
                kind,
                tuneFailure: tuneFailure,
                faults: tuneFailure == "NULL" ? "'[]'::jsonb" : "'[\"TuneFailed\"]'::jsonb",
                gaveUpBecause: gaveUpBecause));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_reservation_outcome_gave_up", refusal.ConstraintName);
    }

    private static TheoryData<string> Named<T>()
        where T : struct, Enum
    {
        var named = new TheoryData<string>();

        foreach (string name in Enum.GetNames<T>())
        {
            named.Add(name);
        }

        return named;
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, Guid reservation, string kind)
    {
        await using var counting = new NpgsqlCommand(
            "SELECT count(*) FROM reservation_outcome WHERE reservation_id = $1 AND kind = $2",
            connection);
        counting.Parameters.Add(new NpgsqlParameter { Value = reservation });
        counting.Parameters.Add(new NpgsqlParameter { Value = kind });

        return (long)(await counting.ExecuteScalarAsync())!;
    }

    private static Task Record(
        NpgsqlConnection connection,
        Guid reservation,
        string kind,
        string? faults = null,
        string? tuneFailure = null,
        string? retryResult = null,
        string? gaveUpBecause = null)
    {
        var command = new NpgsqlCommand(
            $"""
            INSERT INTO reservation_outcome (
                id, reservation_id, network_id, service_id, event_id, programme_start_at,
                snapshot_name, effective_start_at, effective_end_at, priority, rule_id,
                kind, tune_failure, recording_outcome, faults, recorded_instead, occurred_at,
                retry_result, gave_up_because)
            VALUES (
                '{Guid.NewGuid()}', '{reservation}', 47101, 1024, 4001, {Airs},
                'A programme', {Airs}, {Ends}, 50, NULL,
                '{kind}', {tuneFailure ?? TuneFailure(kind)}, {Outcome(kind)}, {faults ?? Faults(kind)}, '[]'::jsonb, {Ends},
                {retryResult ?? RetryResultOf(kind)}, {gaveUpBecause ?? GaveUpBecauseOf(kind)})
            """,
            connection);

        return command.ExecuteNonQueryAsync();
    }

    private static string TuneFailure(string kind)
        => kind == nameof(ReservationOutcomeKind.TuneFailure) ? "'NoLock'" : "NULL";

    private static string Outcome(string kind)
        => kind == nameof(ReservationOutcomeKind.RecordingFailure) ? "'Failed'" : "NULL";

    private static string Faults(string kind)
        => kind == nameof(ReservationOutcomeKind.TuneFailure) ? "'[\"TuneFailed\"]'::jsonb" : "'[]'::jsonb";

    private static string RetryResultOf(string kind)
        => kind == nameof(ReservationOutcomeKind.Retried) ? "'NoAnswer'" : "NULL";

    private static string GaveUpBecauseOf(string kind)
        => kind == nameof(ReservationOutcomeKind.GaveUpRetrying) ? "'AttemptsSpent'" : "NULL";
}
