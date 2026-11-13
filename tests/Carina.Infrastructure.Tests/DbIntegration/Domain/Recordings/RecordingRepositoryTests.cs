using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Recordings;

[Trait("Category", "DbIntegration")]
public sealed class RecordingRepositoryTests(MigratedRepositoryDatabase database)
    : IClassFixture<MigratedRepositoryDatabase>, IAsyncLifetime
{
    private static readonly DateTime Airs = new(2026, 8, 26, 20, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact]
    public async Task ARecordingThatWasWrittenDownIsFoundAgainAsItWasWritten()
    {
        Recording begun = Begin(4001, Airs, Airs.AddMinutes(30));

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Recordings(writing).AddAsync(begun, Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Recording? read = await Recordings(reading).FindAsync(begun.Id, Cancel);

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

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IRecordingRepository repository = Recordings(writing);

            await repository.AddAsync(last, Cancel);
            await repository.AddAsync(first, Cancel);
            await repository.AddAsync(settled, Cancel);

            settled.Abort(Airs.AddMinutes(5));
            settled.Note(new OutcomeDetail(RecordingFault.DriverLost, null, "gone", Airs.AddMinutes(5)));
            settled.Settle(RecordingOutcome.Truncated, 1_000_000, Airs.AddMinutes(5));

            await repository.SaveAsync(settled, Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<Recording> inFlight = await Recordings(reading).ListInFlightAsync(Cancel);

        Assert.Equal([first.Id, last.Id], inFlight.Select(recording => recording.Id));
    }

    [Fact]
    public async Task AnAbortIsStillThereWhenTheRowIsReadBack()
    {
        Recording begun = Begin(4201, Airs, Airs.AddMinutes(30));

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IRecordingRepository repository = Recordings(writing);

            await repository.AddAsync(begun, Cancel);

            begun.Abort(Airs.AddMinutes(30));

            await repository.SaveAsync(begun, Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Recording? read = await Recordings(reading).FindAsync(begun.Id, Cancel);

        Assert.Equal(Airs.AddMinutes(30), read!.AbortedAt);
    }

    [Fact]
    public async Task ARecordingIsFoundByTheReservationItBelongsTo()
    {
        Recording begun = Begin(4301, Airs, Airs.AddMinutes(30));

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Recordings(writing).AddAsync(begun, Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IRecordingRepository repository = Recordings(reading);

        Assert.Equal(
            begun.Id,
            Assert.Single(await repository.ListForReservationAsync(begun.ReservationId!, Cancel)).Id);
        Assert.Empty(await repository.ListForReservationAsync(ReservationId.New(), Cancel));
    }

    [Fact]
    public async Task AWriteRefusedForARowThatMovedMeanwhileDoesNotRideAlongWithTheNextWrite()
    {
        Recording moved = Begin(4301, Airs, Airs.AddMinutes(30));
        Recording next = Begin(4302, Airs, Airs.AddMinutes(30));

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Recordings(writing).AddAsync(moved, Cancel);
        }

        await using AsyncServiceScope round = container.CreateAsyncScope();
        IRecordingRepository repository = Recordings(round);
        Recording held = (await repository.FindAsync(moved.Id, Cancel))!;

        await using (AsyncServiceScope meanwhile = container.CreateAsyncScope())
        {
            IRecordingRepository other = Recordings(meanwhile);
            Recording landed = (await other.FindAsync(moved.Id, Cancel))!;
            landed.Extend(Airs.AddMinutes(40));
            await other.SaveAsync(landed, Cancel);
        }

        held.Extend(Airs.AddMinutes(50));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => repository.SaveAsync(held, Cancel));

        await repository.AddAsync(next, Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IRecordingRepository read = Recordings(reading);

        Assert.Equal(Airs.AddMinutes(40), (await read.FindAsync(moved.Id, Cancel))?.ExpectedWindowEnd);
        Assert.NotNull(await read.FindAsync(next.Id, Cancel));
    }

    private async Task Clear()
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<CarinaDbContext>().Database.ExecuteSqlRawAsync("DELETE FROM recording");
    }

    private static IRecordingRepository Recordings(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IRecordingRepository>();

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
            new ProgrammeSnapshot(
                "A programme",
                string.Empty,
                string.Empty,
                [],
                Airs.AddHours(-6),
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            from,
            new TunerDeviceId("adapter0"));
    }
}
