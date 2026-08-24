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

    private static Recording Begin(int eventId, ReservationId? reservationId = null)
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
            new ProgrammeSnapshot("A programme", "What it is about", string.Empty, [new ProgrammeGenre(7, 1)], Now),
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
