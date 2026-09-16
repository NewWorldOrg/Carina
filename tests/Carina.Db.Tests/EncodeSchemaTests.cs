using Carina.Domain.Encodings;
using Carina.Infrastructure.Persistence.Configurations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeSchemaTests(MigratedScratchDatabase database) : IClassFixture<MigratedScratchDatabase>
{
    private const string Queued = "timestamptz '2026-09-05 03:00:00+00'";

    private const string Started = "timestamptz '2026-09-05 03:00:05+00'";

    private const string Ended = "timestamptz '2026-09-05 04:00:00+00'";

    private static readonly Guid Profile = Guid.Parse("0a1b2c3d-4e5f-4061-8283-8485868788a9");

    private static readonly Guid Destination = Guid.Parse("7c1e2f3a-4b5c-4d6e-8f90-a1b2c3d4e5f6");

    private static readonly string ProfileWire = Profile.ToString("N");

    public static TheoryData<string> Statuses => Named(Enum.GetNames<EncodeJobStatus>());

    public static TheoryData<string> Failures => Named(Enum.GetNames<EncodeFailure>());

    public static TheoryData<string> Verdicts => Named(Enum.GetNames<ChapterVerdict>());

    public static TheoryData<string> Detectors => Named(Enum.GetNames<ChapterDetectorName>());

    public static TheoryData<string> Kinds => Named(Enum.GetNames<ChapterKind>());

    [Fact(DisplayName = "BR-D-004: no encode table holds a foreign key into another domain's table")]
    public async Task NoEncodeTableHoldsAForeignKeyIntoAnotherDomainsTable()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await using var asking = new NpgsqlCommand(
            """
            SELECT declaring.relname || ' -> ' || principal.relname
            FROM pg_constraint AS held
            JOIN pg_class AS declaring ON declaring.oid = held.conrelid
            JOIN pg_class AS principal ON principal.oid = held.confrelid
            WHERE held.contype = 'f' AND declaring.relname LIKE 'encode\_%'
            ORDER BY 1
            """,
            connection);

        List<string> pointing = [];
        await using NpgsqlDataReader reading = await asking.ExecuteReaderAsync();

        while (await reading.ReadAsync())
        {
            pointing.Add(reading.GetString(0));
        }

        Assert.Equal(
            [
                "encode_chapter -> encode_job",
                "encode_destination -> encode_profile",
                "encode_job -> encode_destination",
                "encode_job -> encode_profile",
                "encode_scratch_file -> encode_job",
            ],
            pointing);
    }

    [Fact(DisplayName = "BR-D-004: an encode job names its recording by value, so the ledger can never drag one away")]
    public async Task AnEncodeJobNamesARecordingNoLedgerRowKnows()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);

        await JobAsync(connection, Guid.NewGuid(), Guid.NewGuid(), "'Queued'", "NULL", "NULL", "NULL, NULL, NULL", "NULL");
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public async Task EveryPlaceAJobCanStandAtIsOneTheLedgerTakes(string status)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);
        var recording = Guid.NewGuid();

        await JobAsync(
            connection,
            Guid.NewGuid(),
            recording,
            $"'{status}'",
            status is "Queued" ? "NULL" : Started,
            status is "Queued" or "Running" ? "NULL" : Ended,
            status is "Failed" ? "'TimedOut', 'late', " + Ended : "NULL, NULL, NULL",
            status is "Completed" ? $"'{recording:N}.{ProfileWire}.mp4'" : "NULL");
    }

    [Fact(DisplayName = "BR-ES-002: a sixth place to stand is refused")]
    public async Task ASixthPlaceToStandIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => JobAsync(
            connection, Guid.NewGuid(), Guid.NewGuid(), "'Paused'", "NULL", "NULL", "NULL, NULL, NULL", "NULL"));

        Assert.Equal("ck_encode_job_status", refusal.ConstraintName);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task EveryReasonAJobFailsForIsOneTheLedgerTakes(string failure)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);

        await JobAsync(
            connection, Guid.NewGuid(), Guid.NewGuid(), "'Failed'", Started, Ended, $"'{failure}', 'said', {Ended}", "NULL");
    }

    [Theory]
    [InlineData("'Failed'", "NULL, NULL, NULL")]
    [InlineData("'Completed'", "'TimedOut', 'late', " + Ended)]
    [InlineData("'Failed'", "'Vanished', 'said', " + Ended)]
    [InlineData("'Failed'", "'TimedOut', NULL, " + Ended)]
    [InlineData("'Failed'", "'TimedOut', 'late', NULL")]
    public async Task AFailureIsAClassificationANoteAndATimeTogetherOrNothingAtAll(string status, string failure)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        var recording = Guid.NewGuid();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => JobAsync(
            connection,
            Guid.NewGuid(),
            recording,
            status,
            Started,
            Ended,
            failure,
            status is "'Completed'" ? $"'{recording:N}.{ProfileWire}.mp4'" : "NULL"));

        Assert.Equal("ck_encode_job_failure", refusal.ConstraintName);
    }

    [Theory]
    [InlineData("'Queued'", Started, "NULL")]
    [InlineData("'Running'", "NULL", "NULL")]
    [InlineData("'Running'", Started, Ended)]
    [InlineData("'Cancelled'", "NULL", "NULL")]
    [InlineData("'Running'", "timestamptz '2026-09-05 02:00:00+00'", "NULL")]
    public async Task AJobsTimesAgreeWithWhereItStands(string status, string started, string ended)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => JobAsync(
            connection, Guid.NewGuid(), Guid.NewGuid(), status, started, ended, "NULL, NULL, NULL", "NULL"));

        Assert.Equal("ck_encode_job_timeline", refusal.ConstraintName);
    }

    [Fact(DisplayName = "BR-ED2-009: a completed job names what it made")]
    public async Task ACompletedJobNamesWhatItMade()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => JobAsync(
            connection, Guid.NewGuid(), Guid.NewGuid(), "'Completed'", Started, Ended, "NULL, NULL, NULL", "NULL"));

        Assert.Equal("ck_encode_job_artefact", refusal.ConstraintName);
    }

    [Theory]
    [InlineData("'somebody-elses-name.mp4'")]
    [InlineData("'sub/{recording}.{profile}.mp4'")]
    [InlineData("'../{recording}.{profile}.mp4'")]
    [InlineData("' {recording}.{profile}.mp4'")]
    public async Task AnArtefactIsNamedForItsRecordingAndItsProfileAndIsASingleName(string shape)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        var recording = Guid.NewGuid();
        string name = shape.Replace("{recording}", recording.ToString("N"), StringComparison.Ordinal)
            .Replace("{profile}", ProfileWire, StringComparison.Ordinal);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => JobAsync(
            connection, Guid.NewGuid(), recording, "'Running'", Started, "NULL", "NULL, NULL, NULL", name));

        Assert.Equal("ck_encode_job_artefact", refusal.ConstraintName);
    }

    [Fact(DisplayName = "BR-ED2-009: one name under one root has one owner, and the second claimant is refused by the index")]
    public async Task OneNameUnderOneRootHasOneOwner()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);
        var recording = Guid.NewGuid();
        string name = $"'{recording:N}.{ProfileWire}.mp4'";

        await JobAsync(connection, Guid.NewGuid(), recording, "'Completed'", Started, Ended, "NULL, NULL, NULL", name);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => JobAsync(
            connection, Guid.NewGuid(), recording, "'Running'", Started, "NULL", "NULL, NULL, NULL", name));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refusal.SqlState);
        Assert.Equal(EncodeJobConfiguration.ArtefactIndexName, refusal.ConstraintName);
    }

    [Fact(DisplayName = "A-エンコード-069: the job making the artefact again holds the name once the earlier one has given it up, and not a moment before")]
    public async Task TheJobMakingItAgainHoldsTheNameOnceTheEarlierOneHasGivenItUp()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);
        var recording = Guid.NewGuid();
        var made = Guid.NewGuid();
        string name = $"'{recording:N}.{ProfileWire}.mp4'";

        await JobAsync(connection, made, recording, "'Completed'", Started, Ended, "NULL, NULL, NULL", name);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => JobAsync(
            connection, Guid.NewGuid(), recording, "'Running'", Started, "NULL", "NULL, NULL, NULL", name));

        await using (var givingUp = new NpgsqlCommand(
            $"UPDATE encode_job SET name_given_up_at = {Ended} WHERE id = '{made}'",
            connection))
        {
            Assert.Equal(1, await givingUp.ExecuteNonQueryAsync());
        }

        await JobAsync(connection, Guid.NewGuid(), recording, "'Running'", Started, "NULL", "NULL, NULL, NULL", name);

        await using var counting = new NpgsqlCommand(
            $"SELECT count(*) FROM encode_job WHERE artefact_name = {name}",
            connection);

        Assert.Equal(EncodeJobConfiguration.ArtefactIndexName, refusal.ConstraintName);
        Assert.Equal(2L, await counting.ExecuteScalarAsync());
    }

    [Fact(DisplayName = "A-エンコード-069: a job that named nothing cannot be written down as having given a name up")]
    public async Task AJobThatNamedNothingCannotHaveGivenANameUp()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);
        var waiting = Guid.NewGuid();
        await JobAsync(connection, waiting, Guid.NewGuid(), "'Queued'", "NULL", "NULL", "NULL, NULL, NULL", "NULL");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand(
            $"UPDATE encode_job SET name_given_up_at = {Ended} WHERE id = '{waiting}'",
            connection).ExecuteNonQueryAsync());

        Assert.Equal("ck_encode_job_name_given_up", refusal.ConstraintName);
    }

    [Fact(DisplayName = "BR-ED2-005: the ledger holds one running job, and a second is refused by the index")]
    public async Task TheLedgerHoldsOneRunningJob()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);

        await JobAsync(connection, Guid.NewGuid(), Guid.NewGuid(), "'Running'", Started, "NULL", "NULL, NULL, NULL", "NULL");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => JobAsync(
            connection, Guid.NewGuid(), Guid.NewGuid(), "'Running'", Started, "NULL", "NULL, NULL, NULL", "NULL"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refusal.SqlState);
        Assert.Equal(EncodeJobConfiguration.RunningIndexName, refusal.ConstraintName);
    }

    [Fact]
    public async Task TheIndexesOverJobsAreTheOnesTheDispatcherAndTheLibraryWillRead()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            "CREATE INDEX ix_encode_job_queued ON public.encode_job USING btree (queued_at) "
            + "WHERE ((status)::text = 'Queued'::text)",
            await IndexDefinition(connection, EncodeJobConfiguration.QueuedIndexName));
        Assert.Equal(
            "CREATE UNIQUE INDEX ux_encode_job_running ON public.encode_job USING btree (status) "
            + "WHERE ((status)::text = 'Running'::text)",
            await IndexDefinition(connection, EncodeJobConfiguration.RunningIndexName));
        Assert.Equal(
            "CREATE UNIQUE INDEX ux_encode_job_artefact ON public.encode_job USING btree (output_root, artefact_name) "
            + "WHERE ((artefact_name IS NOT NULL) AND (name_given_up_at IS NULL))",
            await IndexDefinition(connection, EncodeJobConfiguration.ArtefactIndexName));
        Assert.Equal(
            "CREATE INDEX ix_encode_job_recording ON public.encode_job USING btree (recording_id, queued_at)",
            await IndexDefinition(connection, EncodeJobConfiguration.RecordingIndexName));
    }

    [Fact(DisplayName = "BR-ED2-010: a scratch file is settled with a fate and a time together, and by one of the named fates")]
    public async Task AScratchFileIsSettledWithAFateAndATimeTogether()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        var job = Guid.NewGuid();
        await JobAsync(connection, job, Guid.NewGuid(), "'Queued'", "NULL", "NULL", "NULL, NULL, NULL", "NULL");

        foreach (string fate in Enum.GetNames<EncodeScratchFate>())
        {
            await ScratchAsync(connection, job, $"'{Guid.NewGuid():N}.attempt1.encoding'", Ended, $"'{fate}'");
        }

        PostgresException halfSettled = await Assert.ThrowsAsync<PostgresException>(
            () => ScratchAsync(connection, job, "'half.encoding'", Ended, "NULL"));
        PostgresException unnamed = await Assert.ThrowsAsync<PostgresException>(
            () => ScratchAsync(connection, job, "'unnamed.encoding'", Ended, "'Evaporated'"));
        PostgresException backwards = await Assert.ThrowsAsync<PostgresException>(
            () => ScratchAsync(connection, job, "'backwards.encoding'", "timestamptz '2026-09-05 02:00:00+00'", "'Removed'"));

        Assert.Equal("ck_encode_scratch_file_removal", halfSettled.ConstraintName);
        Assert.Equal("ck_encode_scratch_file_removal", unnamed.ConstraintName);
        Assert.Equal("ck_encode_scratch_file_removal", backwards.ConstraintName);
    }

    [Fact(DisplayName = "BR-ED2-010: what is still owed a removal is read off the ledger by job, not off the disk")]
    public async Task WhatIsStillOwedARemovalIsReadOffTheLedgerByJob()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            "CREATE INDEX ix_encode_scratch_file_owed ON public.encode_scratch_file USING btree (job_id) "
            + "WHERE (removed_at IS NULL)",
            await IndexDefinition(connection, EncodeScratchFileConfiguration.OwedIndexName));
        Assert.Equal(
            "CREATE UNIQUE INDEX ux_encode_scratch_file_name ON public.encode_scratch_file USING btree (output_root, file_name)",
            await IndexDefinition(connection, EncodeScratchFileConfiguration.NameIndexName));
    }

    [Theory(DisplayName = "BR-ED2-011: a programme is an id and a start together, on a running job only, begun no earlier than the job started")]
    [InlineData("'Running'", Started, "4242, " + Started, null)]
    [InlineData("'Running'", Started, "NULL, NULL", null)]
    [InlineData("'Running'", Started, "4242, NULL", "ck_encode_job_programme")]
    [InlineData("'Running'", Started, "NULL, " + Started, "ck_encode_job_programme")]
    [InlineData("'Running'", Started, "0, " + Started, "ck_encode_job_programme")]
    [InlineData("'Running'", Started, "4242, " + Queued, "ck_encode_job_programme")]
    [InlineData("'Queued'", "NULL", "4242, " + Started, "ck_encode_job_programme")]
    public async Task AProgrammeIsAnIdAndAStartTogetherOnARunningJobOnly(string status, string started, string programme, string? refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);

        Task writing = MarkedJobAsync(connection, status, started, programme, "NULL, NULL, NULL", "NULL, NULL, NULL", Unaligned);

        if (refusedBy is null)
        {
            await writing;

            return;
        }

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => writing);
        Assert.Equal(refusedBy, refusal.ConstraintName);
    }

    [Theory(DisplayName = "BR-ED2-014: headway is a portion between none and all, what is left, and when — together or not at all, and never on a job that has not run")]
    [InlineData("'Running'", Started, "0.5, interval '00:07:00', " + Ended, null)]
    [InlineData("'Running'", Started, "NULL, NULL, " + Ended, null)]
    [InlineData("'Completed'", Started, "1, interval '0', " + Ended, null)]
    [InlineData("'Running'", Started, "0.5, NULL, NULL", "ck_encode_job_headway")]
    [InlineData("'Running'", Started, "1.5, NULL, " + Ended, "ck_encode_job_headway")]
    [InlineData("'Running'", Started, "0.5, interval '-00:00:01', " + Ended, "ck_encode_job_headway")]
    [InlineData("'Running'", Started, "0.5, NULL, " + Queued, "ck_encode_job_headway")]
    [InlineData("'Queued'", "NULL", "0.5, NULL, " + Ended, "ck_encode_job_headway")]
    public async Task HeadwayIsAPortionWhatIsLeftAndWhenTogetherOrNotAtAll(string status, string started, string headway, string? refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);

        Task writing = MarkedJobAsync(connection, status, started, "NULL, NULL", headway, "NULL, NULL, NULL", Unaligned);

        if (refusedBy is null)
        {
            await writing;

            return;
        }

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => writing);
        Assert.Equal(refusedBy, refusal.ConstraintName);
    }

    [Theory(DisplayName = "BR-EV-004: where a run went is the encoder asked and the encoder run, together, with a swerve exactly when they differ, and only on a job that ran")]
    [InlineData("'Running'", Started, "'Software', 'Software', NULL", null)]
    [InlineData("'Failed'", Started, "'Vaapi', 'Software', 'TheCardIsOutOfReach'", null)]
    [InlineData("'Running'", Started, "'Software', 'Vaapi', 'TheProcessorCannotDoThisCodec'", null)]
    [InlineData("'Running'", Started, "'Software', NULL, NULL", "ck_encode_job_route")]
    [InlineData("'Running'", Started, "'Software', 'Software', 'TheCardIsOutOfReach'", "ck_encode_job_route")]
    [InlineData("'Running'", Started, "'Vaapi', 'Software', NULL", "ck_encode_job_route")]
    [InlineData("'Running'", Started, "'QuickSync', 'Software', 'TheCardIsOutOfReach'", "ck_encode_job_route")]
    [InlineData("'Running'", Started, "'Vaapi', 'Software', 'Whim'", "ck_encode_job_route")]
    [InlineData("'Queued'", "NULL", "'Software', 'Software', NULL", "ck_encode_job_route")]
    public async Task WhereARunWentIsTheEncoderAskedAndRunTogether(string status, string started, string route, string? refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);

        Task writing = MarkedJobAsync(connection, status, started, "NULL, NULL", "NULL, NULL, NULL", route, Unaligned);

        if (refusedBy is null)
        {
            await writing;

            return;
        }

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => writing);
        Assert.Equal(refusedBy, refusal.ConstraintName);
    }

    [Theory(DisplayName = "BR-ED2-006: where the artefact's clock stands is a start and a head skip together, the skip between nothing and five seconds, lengths only beside them, and only on a job that ran")]
    [InlineData("'Running'", Started, "interval '08:28:19.474078', interval '00:00:00.5072', interval '00:34:57.502489', NULL", null)]
    [InlineData("'Completed'", Started, "interval '08:28:19.474078', interval '00:00:00.5072', interval '00:34:57.502489', interval '00:34:56.7947'", null)]
    [InlineData("'Running'", Started, "interval '0', interval '0', NULL, NULL", null)]
    [InlineData("'Running'", Started, "interval '0', interval '00:00:05', NULL, NULL", null)]
    [InlineData("'Failed'", Started, "interval '08:28:19.474078', interval '00:00:00.5072', NULL, NULL", null)]
    [InlineData("'Running'", Started, "interval '08:28:19.474078', NULL, NULL, NULL", "ck_encode_job_alignment")]
    [InlineData("'Running'", Started, "NULL, interval '00:00:00.5072', NULL, NULL", "ck_encode_job_alignment")]
    [InlineData("'Running'", Started, "interval '08:28:19.474078', interval '00:00:05.000001', NULL, NULL", "ck_encode_job_alignment")]
    [InlineData("'Running'", Started, "interval '08:28:19.474078', interval '17:16:10', NULL, NULL", "ck_encode_job_alignment")]
    [InlineData("'Running'", Started, "interval '08:28:19.474078', interval '-00:00:01', NULL, NULL", "ck_encode_job_alignment")]
    [InlineData("'Running'", Started, "interval '-00:00:01', interval '0', NULL, NULL", "ck_encode_job_alignment")]
    [InlineData("'Running'", Started, "NULL, NULL, interval '00:34:57.502489', NULL", "ck_encode_job_alignment")]
    [InlineData("'Running'", Started, "NULL, NULL, NULL, interval '00:34:56.7947'", "ck_encode_job_alignment")]
    [InlineData("'Running'", Started, "interval '0', interval '0', interval '0', NULL", "ck_encode_job_alignment")]
    [InlineData("'Queued'", "NULL", "interval '08:28:19.474078', interval '00:00:00.5072', NULL, NULL", "ck_encode_job_alignment")]
    public async Task WhereTheArtefactsClockStandsIsAStartAndAHeadSkipTogether(string status, string started, string timeline, string? refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);

        Task writing = MarkedJobAsync(connection, status, started, "NULL, NULL", "NULL, NULL, NULL", "NULL, NULL, NULL", timeline);

        if (refusedBy is null)
        {
            await writing;

            return;
        }

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => writing);
        Assert.Equal(refusedBy, refusal.ConstraintName);
    }

    [Fact]
    public async Task TheDatabaseHoldsExactlyTheseChecksOnTheSixTables()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            [
                "ck_encode_profile_codec",
                "ck_encode_profile_deinterlace",
                "ck_encode_profile_label",
                "ck_encode_profile_rate_control",
                "ck_encode_profile_resolution",
            ],
            await ConstraintsAsync(connection, "encode_profile"));
        Assert.Equal(
            ["ck_encode_destination_label", "ck_encode_destination_output_root"],
            await ConstraintsAsync(connection, "encode_destination"));
        Assert.Equal(
            [
                "ck_encode_job_alignment",
                "ck_encode_job_artefact",
                "ck_encode_job_attempt",
                "ck_encode_job_chapters",
                "ck_encode_job_failure",
                "ck_encode_job_headway",
                "ck_encode_job_name_given_up",
                "ck_encode_job_output_root",
                "ck_encode_job_programme",
                "ck_encode_job_route",
                "ck_encode_job_status",
                "ck_encode_job_timeline",
            ],
            await ConstraintsAsync(connection, "encode_job"));
        Assert.Equal(
            ["ck_encode_chapter_kind", "ck_encode_chapter_ordinal", "ck_encode_chapter_span"],
            await ConstraintsAsync(connection, "encode_chapter"));
        Assert.Equal(
            [
                "ck_encode_scratch_file_kind",
                "ck_encode_scratch_file_name",
                "ck_encode_scratch_file_output_root",
                "ck_encode_scratch_file_removal",
            ],
            await ConstraintsAsync(connection, "encode_scratch_file"));
        Assert.Equal(
            ["ck_encode_auto_run_cores", "ck_encode_auto_run_single_row"],
            await ConstraintsAsync(connection, "encode_auto_run"));
    }

    [Theory]
    [InlineData("1, true, 2", null)]
    [InlineData("1, false, 1", null)]
    [InlineData("1, true, 256", null)]
    [InlineData("2, true, 2", "ck_encode_auto_run_single_row")]
    [InlineData("1, true, 0", "ck_encode_auto_run_cores")]
    [InlineData("1, true, 257", "ck_encode_auto_run_cores")]
    public async Task HowTheQueueRunsIsOneRowAndACapTheMachineCouldHold(string values, string? refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using (var clearing = new NpgsqlCommand("DELETE FROM encode_auto_run", connection))
        {
            await clearing.ExecuteNonQueryAsync();
        }

        await using var writing = new NpgsqlCommand(
            $"INSERT INTO encode_auto_run (id, automatically, most_cores, updated_at) VALUES ({values}, {Queued})",
            connection);

        if (refusedBy is null)
        {
            Assert.Equal(1, await writing.ExecuteNonQueryAsync());

            return;
        }

        Assert.Equal(refusedBy, (await Assert.ThrowsAsync<PostgresException>(writing.ExecuteNonQueryAsync)).ConstraintName);
    }

    [Theory]
    [InlineData("'H264', 'AsSource', 'Leave', 23, 24", null)]
    [InlineData("'AV1', 'AsSource', 'Leave', 23, 24", "ck_encode_profile_codec")]
    [InlineData("'H264', 'Cinema', 'Leave', 23, 24", "ck_encode_profile_resolution")]
    [InlineData("'H264', 'AsSource', 'Guess', 23, 24", "ck_encode_profile_deinterlace")]
    [InlineData("'H264', 'AsSource', 'Leave', 52, 24", "ck_encode_profile_rate_control")]
    [InlineData("'H264', 'AsSource', 'Leave', 23, -1", "ck_encode_profile_rate_control")]
    public async Task AProfileIsMadeOfNamedValuesAndNumbersOnAScale(string values, string? refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Task writing = new NpgsqlCommand(
            $"""
            INSERT INTO encode_profile (id, label, codec, resolution, deinterlace, rate_factor, quantiser, defined_at)
            VALUES ('{Guid.NewGuid()}', 'Viewing', {values}, {Queued})
            """,
            connection).ExecuteNonQueryAsync();

        if (refusedBy is null)
        {
            await writing;

            return;
        }

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => writing);

        Assert.Equal(refusedBy, refusal.ConstraintName);
    }

    [Fact(DisplayName = "BR-ED2-015: the database refuses to drop a profile a job points at, which is why a used one is retired instead")]
    public async Task TheDatabaseRefusesToDropAProfileAJobPointsAt()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);
        var recording = Guid.NewGuid();
        await JobAsync(
            connection,
            Guid.NewGuid(),
            recording,
            "'Completed'",
            Started,
            Ended,
            "NULL, NULL, NULL",
            $"'{recording:N}.{ProfileWire}.mp4'");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand(
            $"DELETE FROM encode_profile WHERE id = '{Profile}'",
            connection).ExecuteNonQueryAsync());

        await using var retiring = new NpgsqlCommand(
            $"UPDATE encode_profile SET retired_at = {Ended} WHERE id = '{Profile}'",
            connection);

        Assert.Equal(PostgresErrorCodes.RestrictViolation, refusal.SqlState);
        Assert.Equal(1, await retiring.ExecuteNonQueryAsync());
    }

    [Fact(DisplayName = "BR-ED2-015: a definition nothing points at is dropped, and both tables leave the retirement hour empty until it comes")]
    public async Task ADefinitionNothingPointsAtIsDropped()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);
        var spare = Guid.NewGuid();

        await using var defining = new NpgsqlCommand(
            $"""
            INSERT INTO encode_profile (id, label, codec, resolution, deinterlace, rate_factor, quantiser, defined_at)
            VALUES ('{spare}', 'Nobody asked for this', 'H264', 'AsSource', 'Leave', 23, 24, {Queued})
            """,
            connection);
        await defining.ExecuteNonQueryAsync();

        await using var asking = new NpgsqlCommand(
            $"SELECT retired_at IS NULL FROM encode_profile WHERE id = '{spare}'",
            connection);
        bool unretired = (bool)(await asking.ExecuteScalarAsync())!;

        await using var dropping = new NpgsqlCommand($"DELETE FROM encode_profile WHERE id = '{spare}'", connection);

        Assert.True(unretired);
        Assert.Equal(1, await dropping.ExecuteNonQueryAsync());
    }

    [Theory(DisplayName = "A-エンコード-057: what a run made of the breaks is a reader, a verdict, a share and a time together or none of them, and only on a job that ran")]
    [InlineData("'Running'", Started, "'Ffmpeg', 'Marked', 0.31, " + Ended, null)]
    [InlineData("'Running'", Started, "'Nobody', 'NotAsked', 0, " + Ended, null)]
    [InlineData("'Completed'", Started, "'Ffmpeg', 'Discarded', 1, " + Ended, null)]
    [InlineData("'Running'", Started, "NULL, NULL, NULL, NULL", null)]
    [InlineData("'Running'", Started, "'Ffmpeg', NULL, NULL, NULL", "ck_encode_job_chapters")]
    [InlineData("'Running'", Started, "NULL, 'Marked', 0.31, " + Ended, "ck_encode_job_chapters")]
    [InlineData("'Running'", Started, "'Ffmpeg', 'Marked', NULL, " + Ended, "ck_encode_job_chapters")]
    [InlineData("'Running'", Started, "'Ffmpeg', 'Marked', 0.31, NULL", "ck_encode_job_chapters")]
    [InlineData("'Running'", Started, "'Ffmpeg', 'Guessed', 0.31, " + Ended, "ck_encode_job_chapters")]
    [InlineData("'Running'", Started, "'Somebody', 'Marked', 0.31, " + Ended, "ck_encode_job_chapters")]
    [InlineData("'Running'", Started, "'Ffmpeg', 'Marked', 1.01, " + Ended, "ck_encode_job_chapters")]
    [InlineData("'Running'", Started, "'Ffmpeg', 'Marked', -0.01, " + Ended, "ck_encode_job_chapters")]
    [InlineData("'Running'", Started, "'Ffmpeg', 'Marked', 0.31, " + Queued, "ck_encode_job_chapters")]
    [InlineData("'Queued'", "NULL", "'Ffmpeg', 'Marked', 0.31, " + Ended, "ck_encode_job_chapters")]
    public async Task WhatARunMadeOfTheBreaksIsAReaderAVerdictAShareAndATimeTogether(
        string status,
        string started,
        string chapters,
        string? refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);

        Task writing = MarkedJobAsync(connection, status, started, "NULL, NULL", "NULL, NULL, NULL", "NULL, NULL, NULL", Unaligned, chapters);

        if (refusedBy is null)
        {
            await writing;

            return;
        }

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => writing);
        Assert.Equal(refusedBy, refusal.ConstraintName);
    }

    [Theory]
    [MemberData(nameof(Verdicts))]
    public async Task EveryWayAReadingCanEndIsOneTheLedgerTakes(string verdict)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);

        await MarkedJobAsync(
            connection,
            "'Running'",
            Started,
            "NULL, NULL",
            "NULL, NULL, NULL",
            "NULL, NULL, NULL",
            Unaligned,
            $"'Ffmpeg', '{verdict}', 0, {Ended}");
    }

    [Theory]
    [MemberData(nameof(Detectors))]
    public async Task EveryReaderThatCanAnswerIsOneTheLedgerTakes(string detector)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);

        await MarkedJobAsync(
            connection,
            "'Running'",
            Started,
            "NULL, NULL",
            "NULL, NULL, NULL",
            "NULL, NULL, NULL",
            Unaligned,
            $"'{detector}', 'NotAsked', 0, {Ended}");
    }

    [Theory(DisplayName = "A-エンコード-057: a chapter belongs to a job, ends after it starts, starts no earlier than the artefact does, and is one of the kinds named")]
    [InlineData(0, "interval '0'", "interval '00:00:30'", "'Programme'", null)]
    [InlineData(1, "interval '00:00:30'", "interval '00:01:30'", "'Break'", null)]
    [InlineData(0, "interval '00:00:30'", "interval '00:00:30'", "'Programme'", "ck_encode_chapter_span")]
    [InlineData(0, "interval '00:00:30'", "interval '00:00:29'", "'Programme'", "ck_encode_chapter_span")]
    [InlineData(0, "interval '-00:00:01'", "interval '00:00:30'", "'Programme'", "ck_encode_chapter_span")]
    [InlineData(-1, "interval '0'", "interval '00:00:30'", "'Programme'", "ck_encode_chapter_ordinal")]
    [InlineData(0, "interval '0'", "interval '00:00:30'", "'Advertisement'", "ck_encode_chapter_kind")]
    public async Task AChapterBelongsToAJobAndEndsAfterItStarts(
        int ordinal,
        string startsAt,
        string endsAt,
        string kind,
        string? refusedBy)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);
        var job = Guid.NewGuid();
        await JobAsync(connection, job, Guid.NewGuid(), "'Running'", Started, "NULL", "NULL, NULL, NULL", "NULL");

        Task writing = ChapterAsync(connection, job, ordinal, startsAt, endsAt, kind);

        if (refusedBy is null)
        {
            await writing;

            return;
        }

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => writing);
        Assert.Equal(refusedBy, refusal.ConstraintName);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task EveryKindOfChapterIsOneTheLedgerTakes(string kind)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);
        var job = Guid.NewGuid();
        await JobAsync(connection, job, Guid.NewGuid(), "'Running'", Started, "NULL", "NULL, NULL, NULL", "NULL");

        await ChapterAsync(connection, job, 0, "interval '0'", "interval '00:00:30'", $"'{kind}'");
    }

    [Fact(DisplayName = "A-エンコード-057: one job holds one chapter at each place in the order, and the second claimant is refused by the index")]
    public async Task OneJobHoldsOneChapterAtEachPlaceInTheOrder()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);
        var job = Guid.NewGuid();
        var other = Guid.NewGuid();
        await JobAsync(connection, job, Guid.NewGuid(), "'Running'", Started, "NULL", "NULL, NULL, NULL", "NULL");
        await JobAsync(connection, other, Guid.NewGuid(), "'Queued'", "NULL", "NULL", "NULL, NULL, NULL", "NULL");

        await ChapterAsync(connection, job, 0, "interval '0'", "interval '00:00:30'", "'Programme'");
        await ChapterAsync(connection, other, 0, "interval '0'", "interval '00:00:30'", "'Programme'");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => ChapterAsync(connection, job, 0, "interval '00:00:30'", "interval '00:01:30'", "'Break'"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refusal.SqlState);
        Assert.Equal(EncodeChapterConfiguration.OrdinalIndexName, refusal.ConstraintName);
    }

    [Fact(DisplayName = "A-エンコード-057: the database refuses to drop a job whose chapters are still held, so a reading is never left pointing at nothing")]
    public async Task TheDatabaseRefusesToDropAJobWhoseChaptersAreStillHeld()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await SeedAsync(connection);
        await ClearJobsAsync(connection);
        var job = Guid.NewGuid();
        await JobAsync(connection, job, Guid.NewGuid(), "'Running'", Started, "NULL", "NULL, NULL, NULL", "NULL");
        await ChapterAsync(connection, job, 0, "interval '0'", "interval '00:00:30'", "'Programme'");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand(
            $"DELETE FROM encode_job WHERE id = '{job}'",
            connection).ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.RestrictViolation, refusal.SqlState);
    }

    private static TheoryData<string> Named(IEnumerable<string> names)
    {
        var named = new TheoryData<string>();

        foreach (string name in names)
        {
            named.Add(name);
        }

        return named;
    }

    private static async Task SeedAsync(NpgsqlConnection connection)
    {
        await using var seeding = new NpgsqlCommand(
            $"""
            INSERT INTO encode_profile (id, label, codec, resolution, deinterlace, rate_factor, quantiser, defined_at)
            VALUES ('{Profile}', 'Viewing', 'H264', 'AsSource', 'Leave', 23, 24, {Queued})
            ON CONFLICT (id) DO NOTHING;
            INSERT INTO encode_destination (id, label, output_root, default_profile_id, defined_at)
            VALUES ('{Destination}', 'Primary', 'primary', '{Profile}', {Queued})
            ON CONFLICT (id) DO NOTHING;
            """,
            connection);

        await seeding.ExecuteNonQueryAsync();
    }

    private static async Task ClearJobsAsync(NpgsqlConnection connection)
    {
        await using var clearing = new NpgsqlCommand(
            "DELETE FROM encode_chapter; DELETE FROM encode_scratch_file; DELETE FROM encode_job",
            connection);

        await clearing.ExecuteNonQueryAsync();
    }

    private static Task JobAsync(
        NpgsqlConnection connection,
        Guid id,
        Guid recording,
        string status,
        string started,
        string ended,
        string failure,
        string artefact)
        => new NpgsqlCommand(
            $"""
            INSERT INTO encode_job (
                id, recording_id, profile_id, destination_id, output_root, status, attempt,
                queued_at, started_at, ended_at, failure, failure_note, failure_noticed_at, artefact_name)
            VALUES (
                '{id}', '{recording}', '{Profile}', '{Destination}', 'primary', {status}, 1,
                {Queued}, {started}, {ended}, {failure}, {artefact})
            """,
            connection).ExecuteNonQueryAsync();

    private const string Unaligned = "NULL, NULL, NULL, NULL";

    private const string Unjudged = "NULL, NULL, NULL, NULL";

    private static Task MarkedJobAsync(
        NpgsqlConnection connection,
        string status,
        string started,
        string programme,
        string headway,
        string route,
        string timeline,
        string chapters = Unjudged)
    {
        bool ended = status is "'Completed'" or "'Failed'";
        var recording = Guid.NewGuid();
        string failure = status is "'Failed'" ? "'TimedOut', 'late', " + Ended : "NULL, NULL, NULL";
        string artefact = status is "'Completed'" ? $"'{recording:N}.{ProfileWire}.mp4'" : "NULL";

        return new NpgsqlCommand(
            $"""
            INSERT INTO encode_job (
                id, recording_id, profile_id, destination_id, output_root, status, attempt,
                queued_at, started_at, ended_at, failure, failure_note, failure_noticed_at, artefact_name,
                process_id, process_started_at, progress_portion, progress_left, progress_at,
                encoder_asked, encoder_ran, swerve,
                source_start, head_skip, source_length, artefact_length,
                chapters_detector, chapters_verdict, chapters_break_share, chapters_decided_at)
            VALUES (
                '{Guid.NewGuid()}', '{recording}', '{Profile}', '{Destination}', 'primary', {status}, 1,
                {Queued}, {started}, {(ended ? Ended : "NULL")}, {failure}, {artefact},
                {programme}, {headway}, {route}, {timeline}, {chapters})
            """,
            connection).ExecuteNonQueryAsync();
    }

    private static Task ChapterAsync(
        NpgsqlConnection connection,
        Guid job,
        int ordinal,
        string startsAt,
        string endsAt,
        string kind)
        => new NpgsqlCommand(
            $"""
            INSERT INTO encode_chapter (id, job_id, ordinal, starts_at, ends_at, kind)
            VALUES ('{Guid.NewGuid()}', '{job}', {ordinal}, {startsAt}, {endsAt}, {kind})
            """,
            connection).ExecuteNonQueryAsync();

    private static Task ScratchAsync(NpgsqlConnection connection, Guid job, string name, string removedAt, string fate)
        => new NpgsqlCommand(
            $"""
            INSERT INTO encode_scratch_file (id, job_id, kind, output_root, file_name, written_at, removed_at, fate)
            VALUES ('{Guid.NewGuid()}', '{job}', 'WorkFile', 'primary', {name}, {Queued}, {removedAt}, {fate})
            """,
            connection).ExecuteNonQueryAsync();

    private static async Task<string> IndexDefinition(NpgsqlConnection connection, string name)
    {
        await using var reading = new NpgsqlCommand($"SELECT indexdef FROM pg_indexes WHERE indexname = '{name}'", connection);

        return (string)(await reading.ExecuteScalarAsync())!;
    }

    private static async Task<IReadOnlyList<string>> ConstraintsAsync(NpgsqlConnection connection, string table)
    {
        await using var reading = new NpgsqlCommand(
            $"""
            SELECT conname FROM pg_constraint
            WHERE conrelid = '{table}'::regclass AND contype = 'c'
            ORDER BY conname
            """,
            connection);

        List<string> names = [];
        await using NpgsqlDataReader row = await reading.ExecuteReaderAsync();

        while (await row.ReadAsync())
        {
            names.Add(row.GetString(0));
        }

        return names;
    }
}
