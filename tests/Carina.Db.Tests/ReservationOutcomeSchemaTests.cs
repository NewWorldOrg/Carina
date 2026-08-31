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

    public static TheoryData<string> Kinds
    {
        get
        {
            var named = new TheoryData<string>();

            foreach (ReservationOutcomeKind kind in Enum.GetValues<ReservationOutcomeKind>())
            {
                named.Add(kind.ToString());
            }

            return named;
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task EveryKindTheApplicationCanNameIsOneTheLedgerTakes(string kind)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, Guid.NewGuid(), kind);
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

    private static Task Record(NpgsqlConnection connection, Guid reservation, string kind)
    {
        var command = new NpgsqlCommand(
            $"""
            INSERT INTO reservation_outcome (
                id, reservation_id, network_id, service_id, event_id, programme_start_at,
                snapshot_name, effective_start_at, effective_end_at, priority, rule_id,
                kind, tune_failure, recording_outcome, recorded_instead, occurred_at)
            VALUES (
                '{Guid.NewGuid()}', '{reservation}', 47101, 1024, 4001, {Airs},
                'A programme', {Airs}, {Ends}, 50, NULL,
                '{kind}', {TuneFailure(kind)}, {Outcome(kind)}, '[]'::jsonb, {Ends})
            """,
            connection);

        return command.ExecuteNonQueryAsync();
    }

    private static string TuneFailure(string kind)
        => kind == nameof(ReservationOutcomeKind.TuneFailure) ? "'NoLock'" : "NULL";

    private static string Outcome(string kind)
        => kind == nameof(ReservationOutcomeKind.RecordingFailure) ? "'Failed'" : "NULL";
}
