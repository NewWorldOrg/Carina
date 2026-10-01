using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingWriteThroughTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Now = new(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task WhatWasWrittenAddsUpAcrossTheSavesThatCarryIt()
    {
        Recording recording = Begin(60101);
        await Add(recording);

        await Reload(recording.Id, loaded => loaded.Wrote(TimeSpan.FromMinutes(10)));
        await Reload(recording.Id, loaded => loaded.Wrote(TimeSpan.FromMinutes(12)));

        await using CarinaDbContext context = Context();
        Recording settled = await Load(context, recording.Id);

        Assert.Equal(TimeSpan.FromMinutes(22), settled.Written);
        Assert.Equal(1_320_000, settled.WrittenDurationMs);
    }

    [Fact]
    public async Task TheSecondOfTwoAdditionsIsRefusedRatherThanQuietlyDropped()
    {
        Recording recording = Begin(60102);
        await Add(recording);

        await using CarinaDbContext first = Context();
        await using CarinaDbContext second = Context();
        Recording mine = await Load(first, recording.Id);
        Recording theirs = await Load(second, recording.Id);

        mine.Wrote(TimeSpan.FromMinutes(10));
        theirs.Wrote(TimeSpan.FromMinutes(12));

        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using CarinaDbContext reader = Context();
        Recording read = await Load(reader, recording.Id);

        Assert.Equal(600_000, read.WrittenDurationMs);
        Assert.NotEqual(1_320_000, read.WrittenDurationMs);
    }

    [Fact]
    public async Task AMeasurementAndAnExtensionOnTheSameRowCannotBothLandUnnoticed()
    {
        Recording recording = Begin(60104);
        recording.Acquire(new TunerDeviceId("pt3-1"));
        await Add(recording);

        await using CarinaDbContext measuring = Context();
        await using CarinaDbContext extending = Context();
        Recording counted = await Load(measuring, recording.Id);
        Recording followed = await Load(extending, recording.Id);

        counted.Measure(DropCounters.Counted(3, 1000), DropTimeline.Unlocated, null, 0, Now.AddMinutes(20));
        followed.Extend(followed.ExpectedWindowEnd.AddMinutes(15));

        await measuring.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => extending.SaveChangesAsync());

        await using CarinaDbContext reader = Context();
        Recording read = await Load(reader, recording.Id);

        Assert.Equal(DropCounters.Counted(3, 1000), read.Counters);
        Assert.Equal(Now.AddHours(1), read.ExpectedWindowEnd);
    }

    [Fact]
    public async Task AWriterThatReadsAgainAfterBeingRefusedAddsOnTopOfWhatLanded()
    {
        Recording recording = Begin(60105);
        await Add(recording);

        await using (CarinaDbContext first = Context())
        await using (CarinaDbContext second = Context())
        {
            Recording mine = await Load(first, recording.Id);
            Recording theirs = await Load(second, recording.Id);

            mine.Wrote(TimeSpan.FromMinutes(10));
            theirs.Wrote(TimeSpan.FromMinutes(12));

            await first.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        }

        await Reload(recording.Id, loaded => loaded.Wrote(TimeSpan.FromMinutes(12)));

        await using CarinaDbContext reader = Context();
        Recording read = await Load(reader, recording.Id);

        Assert.Equal(1_320_000, read.WrittenDurationMs);
    }

    [Fact]
    public async Task ARecordingSurvivesTheRoundTripWithEverythingItCarries()
    {
        Recording recording = Begin(60103, new ReservationId(Guid.NewGuid()));
        recording.Acquire(new TunerDeviceId("pt3-1"));
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Interrupt(RecordingFault.DriverLost, Now.AddMinutes(10));
        recording.Resume(Now.AddMinutes(10).AddSeconds(9));
        recording.Measure(
            DropCounters.Counted(3, 1000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 3, 0)], [new PcrReanchor(20, 8_589_934_591, 0)]),
            null,
            2,
            Now.AddMinutes(20));
        recording.Note(new OutcomeDetail(RecordingFault.ScramblingUnresolved, null, "card", Now.AddMinutes(25)));
        recording.Abort(Now.AddMinutes(60));
        recording.Settle(RecordingOutcome.Truncated, 1_200_000, Now.AddMinutes(60));
        recording.Illustrate(ThumbnailState.Ready);

        await Add(recording);

        await using CarinaDbContext context = Context();
        Recording read = await Load(context, recording.Id);

        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Equal(1, read.ResumeCount);
        Assert.Equal(RecordingFault.DriverLost, Assert.Single(read.Interruptions).Fault);
        Assert.Equal(DateTimeKind.Utc, Assert.Single(read.Interruptions).OccurredAt.Kind);
        Assert.Equal(RecordingFault.ScramblingUnresolved, Assert.Single(read.OutcomeDetail).Fault);
        Assert.Equal(DateTimeKind.Utc, Assert.Single(read.OutcomeDetail).NoticedAt.Kind);
        Assert.Equal(DropCounters.Counted(3, 1000), read.Counters);
        Assert.True(read.Positions.Located);
        Assert.Equal(900_000, read.Positions.AnchorPcr);
        Assert.Equal(12, Assert.Single(read.Positions.Buckets).Second);
        Assert.Equal(8_589_934_591, Assert.Single(read.Positions.Reanchors).Before);
        Assert.Equal(new TunerDeviceId("pt3-1"), read.TunerDeviceId);
        Assert.Equal(ThumbnailState.Ready, read.ThumbnailState);
        Assert.Equal(2, read.EovfCount);
    }

    [Fact]
    public async Task TheSoundABroadcastAnnouncedSurvivesTheRoundTripBesideTheNameItWasRecordedUnder()
    {
        Recording recording = Begin(60106, audio: AudioMode.DualMono, sounds: 2);
        await Add(recording);

        await using CarinaDbContext context = Context();
        Recording read = await Load(context, recording.Id);

        Assert.Equal(AudioMode.DualMono, read.SnapshotAudio);
        Assert.Equal(2, read.SnapshotSounds);
    }

    [Fact]
    public async Task ARecordingOfABroadcastThatAnnouncedNoSoundLandsSayingSo()
    {
        Recording recording = Begin(60107);
        await Add(recording);

        await using CarinaDbContext context = Context();
        Recording read = await Load(context, recording.Id);

        Assert.Equal(AudioMode.Undetermined, read.SnapshotAudio);
        Assert.Equal(ProgrammeSnapshot.SoundsUnannounced, read.SnapshotSounds);
    }

    [Fact]
    public async Task ASoundTheSystemHasNoWordForNeverLandsOnARecording()
    {
        Recording recording = Begin(60108);
        await Add(recording);

        Assert.Equal(
            "ck_recording_snapshot_audio",
            await Refused($"UPDATE recording SET snapshot_audio = 'Quadraphonic' WHERE id = '{recording.Id.Value}'"));
        Assert.Equal(
            "ck_recording_snapshot_sounds",
            await Refused($"UPDATE recording SET snapshot_sounds = -1 WHERE id = '{recording.Id.Value}'"));
    }

    private async Task<string?> Refused(string sql)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        PostgresException refusal =
            await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);

        return refusal.ConstraintName;
    }

    [Fact]
    public async Task EveryExtensionWrittenThroughLeavesTheEndARecordingWasPromisedWhereItBegan()
    {
        Recording recording = Begin(60109);
        await Add(recording);

        await Reload(recording.Id, loaded => loaded.Extend(loaded.ExpectedWindowEnd.AddMinutes(15)));
        await Reload(recording.Id, loaded => loaded.Extend(loaded.ExpectedWindowEnd.AddMinutes(10)));

        await using CarinaDbContext reader = Context();
        Recording read = await Load(reader, recording.Id);

        Assert.Equal(Now.AddHours(1).AddMinutes(25), read.ExpectedWindowEnd);
        Assert.Equal(Now.AddHours(1), read.PromisedWindowEnd);
    }

    [Fact]
    public async Task AGapKeptOnARecordingIsReadBackWithWhatItMissed()
    {
        Recording recording = Begin(60120);
        await Add(recording);

        await Reload(
            recording.Id,
            loaded => loaded.Missed(new RecordingGap(Now.AddMinutes(5).AddSeconds(12), Now.AddMinutes(5).AddSeconds(14.5))));

        await using CarinaDbContext context = Context();
        Recording read = await Load(context, recording.Id);
        RecordingGap gap = Assert.Single(read.Gaps);

        Assert.Equal(Now.AddMinutes(5).AddSeconds(12), gap.From);
        Assert.Equal(DateTimeKind.Utc, gap.From.Kind);
        Assert.Equal(2_500, read.MissedMs);
    }

    [Fact]
    public async Task ARecordingThatNeverMissedAnythingIsReadBackWithoutAGap()
    {
        Recording recording = Begin(60121);
        await Add(recording);

        await using CarinaDbContext context = Context();
        Recording read = await Load(context, recording.Id);

        Assert.Empty(read.Gaps);
        Assert.Equal(0, read.MissedMs);
    }

    [Theory]
    [InlineData("gaps = '[{\"from\":\"2026-08-24T20:05:12Z\",\"until\":\"2026-08-24T20:05:14.5Z\"}]'::jsonb")]
    [InlineData("missed_ms = 2500")]
    [InlineData("gaps = '{}'::jsonb")]
    public async Task WhatARecordingMissedAndTheGapsItKeptCannotDisagree(string change)
    {
        Recording recording = Begin(60122 + change.Length);
        await Add(recording);

        await using NpgsqlConnection connection = await database.OpenAsync();
        await using NpgsqlCommand command = new($"UPDATE recording SET {change} WHERE id = '{recording.Id.Value}'", connection);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_gaps", refusal.ConstraintName);
    }

    [Fact(DisplayName = "what earlier sessions counted and which session is being counted are read back, and the session read back is not counted twice")]
    public async Task WhatEarlierSessionsCountedAndWhichSessionIsBeingCountedAreReadBack()
    {
        Recording recording = Begin(60130);
        recording.Acquire(new TunerDeviceId("pt3-1"));
        await Add(recording);
        DateTime opened = Now.AddTicks(1_234_567);
        DateTime reopened = Now.AddMinutes(10).AddTicks(7_654_321);
        DropTimeline placedAgain = DropTimeline.Rehydrate(
            900_000 + (602 * DropTimeline.TicksPerSecond),
            [new DropBucket(5, 2, 0)],
            []);

        await Reload(
            recording.Id,
            loaded => loaded.Measure(
                DropCounters.Counted(3, 1_000),
                DropTimeline.Rehydrate(900_000, [new DropBucket(12, 3, 0)], []),
                20,
                1,
                Now.AddMinutes(9),
                opened));
        await Reload(
            recording.Id,
            loaded => loaded.Measure(DropCounters.Counted(2, 500), placedAgain, 5, 2, Now.AddMinutes(12), reopened));
        await Reload(
            recording.Id,
            loaded => loaded.Measure(DropCounters.Counted(4, 900), placedAgain, 6, 2, Now.AddMinutes(13), reopened));

        await using CarinaDbContext context = Context();
        Recording read = await Load(context, recording.Id);

        Assert.Equal(DropCounters.Counted(7, 1_900), read.Counters);
        Assert.Equal(26, read.ScrambledPackets);
        Assert.Equal(3, read.EovfCount);
        Assert.Equal([new DropBucket(12, 3, 0), new DropBucket(607, 2, 0)], read.Positions.Buckets);
        Assert.Equal(DropCounters.Counted(3, 1_000), read.Carried.Counters);
        Assert.Equal(20, read.Carried.ScrambledPackets);
        Assert.Equal(1, read.Carried.Overflows);
        Assert.Equal(900_000, read.Carried.Positions.AnchorPcr);
        Assert.Equal([new DropBucket(12, 3, 0)], read.Carried.Positions.Buckets);
        Assert.Equal(Now.AddMinutes(10).AddMilliseconds(765), read.CountedSessionOpenedAt);
        Assert.Equal(DateTimeKind.Utc, read.CountedSessionOpenedAt!.Value.Kind);
    }

    [Fact]
    public async Task ARecordingOnlyOneSessionWroteIsReadBackCarryingNothing()
    {
        Recording recording = Begin(60131);
        await Add(recording);

        await using CarinaDbContext context = Context();
        Recording read = await Load(context, recording.Id);

        Assert.Equal(DropCounters.Unmeasured, read.Carried.Counters);
        Assert.False(read.Carried.Positions.Located);
        Assert.Null(read.Carried.ScrambledPackets);
        Assert.Equal(0, read.Carried.Overflows);
        Assert.Null(read.CountedSessionOpenedAt);
    }

    [Theory]
    [InlineData("carried_eovf_count = 2")]
    [InlineData("carried_cc_dropped_packets = 0")]
    [InlineData("carried_cc_dropped_packets = 4, carried_cc_total_packets = 1000")]
    [InlineData("carried_cc_dropped_packets = 0, carried_cc_total_packets = 1001")]
    [InlineData("carried_scrambled_packets = 21")]
    [InlineData("carried_pcr_anchor = 900000")]
    [InlineData("carried_drop_positions = '[{\"second\":1,\"continuity\":1,\"scrambled\":0}]'::jsonb")]
    [InlineData("carried_cc_dropped_packets = 1, carried_cc_total_packets = 10, carried_pcr_anchor = 900000, carried_drop_positions = '[{\"second\":1,\"continuity\":2,\"scrambled\":0}]'::jsonb")]
    public async Task ARecordingCannotCarryMoreThanItCountsNorPlaceWhatItDoesNotCarry(string change)
    {
        Recording recording = Begin(60132 + change.Length);
        recording.Acquire(new TunerDeviceId("pt3-1"));
        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 20, 1, Now.AddMinutes(9), Now);
        await Add(recording);

        await using NpgsqlConnection connection = await database.OpenAsync();
        await using NpgsqlCommand command = new($"UPDATE recording SET {change} WHERE id = '{recording.Id.Value}'", connection);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_what_was_carried", refusal.ConstraintName);
    }

    private static Recording Begin(
        int eventId,
        ReservationId? reservationId = null,
        AudioMode audio = AudioMode.Undetermined,
        int sounds = ProgrammeSnapshot.SoundsUnannounced)
    {
        RecordingId id = RecordingId.New();

        return Recording.Begin(
            id,
            reservationId,
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(eventId), Now),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            Now,
            Now.AddHours(1),
            new ProgrammeSnapshot(
                "A programme",
                "What it is about",
                string.Empty,
                [new ProgrammeGenre(7, 1)],
                Now,
                audio,
                sounds),
            null,
            BroadcastGroupRole.Standalone,
            Now);
    }

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);

    private async Task Add(Recording recording)
    {
        await using CarinaDbContext context = Context();
        context.Add(recording);
        await context.SaveChangesAsync();
    }

    private async Task Reload(RecordingId id, Action<Recording> change)
    {
        await using CarinaDbContext context = Context();
        Recording loaded = await Load(context, id);
        change(loaded);
        await context.SaveChangesAsync();
    }

    private static Task<Recording> Load(CarinaDbContext context, RecordingId id)
        => context.Set<Recording>().SingleAsync(recording => recording.Id == id);
}
