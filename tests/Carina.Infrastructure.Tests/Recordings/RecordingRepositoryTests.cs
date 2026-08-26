using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Recordings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime Airs = new(2026, 8, 26, 20, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ARecordingThatWasWrittenDownIsFoundAgainAsItWasWritten()
    {
        Recording begun = Begin(4001, Airs, Airs.AddMinutes(30));

        await using (CarinaDbContext writing = database.Open())
        {
            await new RecordingRepository(writing).AddAsync(begun, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        Recording? read = await new RecordingRepository(reading).FindAsync(begun.Id, Cancel);

        Assert.NotNull(read);
        Assert.Equal(Airs, read.ExpectedWindowStart);
        Assert.Equal(Airs.AddMinutes(30), read.ExpectedWindowEnd);
        Assert.Equal($"{begun.Id.Wire}.ts", read.FileName.Value);
        Assert.True(read.IsInFlight);
        Assert.Null(read.AbortedAt);
    }

    [Fact]
    public async Task WhatIsStillInFlightComesBackInTheOrderItHasToBeStoppedIn()
    {
        await Clear();

        Recording last = Begin(4102, Airs, Airs.AddMinutes(45));
        Recording first = Begin(4101, Airs, Airs.AddMinutes(15));
        Recording settled = Begin(4103, Airs, Airs.AddMinutes(5));

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new RecordingRepository(writing);

            await repository.AddAsync(last, Cancel);
            await repository.AddAsync(first, Cancel);
            await repository.AddAsync(settled, Cancel);

            settled.Abort(Airs.AddMinutes(5));
            settled.Note(new OutcomeDetail(RecordingFault.DriverLost, null, "gone", Airs.AddMinutes(5)));
            settled.Settle(RecordingOutcome.Truncated, 1_000_000, Airs.AddMinutes(5));

            await repository.SaveAsync(settled, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<Recording> inFlight = await new RecordingRepository(reading).ListInFlightAsync(Cancel);

        Assert.Equal([first.Id, last.Id], inFlight.Select(recording => recording.Id));
    }

    [Fact]
    public async Task AnAbortIsStillThereWhenTheRowIsReadBack()
    {
        Recording begun = Begin(4201, Airs, Airs.AddMinutes(30));

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new RecordingRepository(writing);

            await repository.AddAsync(begun, Cancel);

            begun.Abort(Airs.AddMinutes(30));

            await repository.SaveAsync(begun, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        Recording? read = await new RecordingRepository(reading).FindAsync(begun.Id, Cancel);

        Assert.Equal(Airs.AddMinutes(30), read!.AbortedAt);
    }

    [Fact]
    public async Task ARecordingIsFoundByTheReservationItBelongsTo()
    {
        Recording begun = Begin(4301, Airs, Airs.AddMinutes(30));

        await using (CarinaDbContext writing = database.Open())
        {
            await new RecordingRepository(writing).AddAsync(begun, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        var repository = new RecordingRepository(reading);

        Assert.Equal(
            begun.Id,
            Assert.Single(await repository.ListForReservationAsync(begun.ReservationId!, Cancel)).Id);
        Assert.Empty(await repository.ListForReservationAsync(ReservationId.New(), Cancel));
    }

    private async Task Clear()
    {
        await using CarinaDbContext context = database.Open();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM recording");
    }

    private static Recording Begin(int eventId, DateTime from, DateTime until)
    {
        RecordingId id = RecordingId.New();

        return Recording.Begin(
            id,
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(eventId), Airs),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".ts"),
            from,
            until,
            new ProgrammeSnapshot("A programme", string.Empty, string.Empty, [], Airs.AddHours(-6)),
            null,
            BroadcastGroupRole.Standalone,
            from,
            new TunerDeviceId("adapter0"));
    }
}
