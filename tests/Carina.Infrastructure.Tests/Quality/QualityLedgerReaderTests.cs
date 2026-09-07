using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Quality;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class QualityLedgerReaderTests(RepositoryDatabase database)
{
    private static readonly DateTime Airs = new(2026, 9, 7, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task OnlyTheRecordingsInsideThePeriodComeBack()
    {
        await ClearAsync();
        Recording inside = await WrittenAsync(6001, Airs);
        await WrittenAsync(6002, Airs.AddDays(-8));

        IReadOnlyList<QualityLedgerRow> read = await ReadAsync(Now.AddDays(-1), Now);

        Assert.Equal([inside.Id], read.Select(row => row.Recording));
    }

    [Fact]
    public async Task WhatTheLedgerCountedComesBackAsItWasCounted()
    {
        await ClearAsync();
        Recording measured = await WrittenAsync(6011, Airs, dropped: 117, total: 741_375, scrambled: 27, overflows: 2);

        QualityLedgerRow row = Assert.Single(await ReadAsync(Now.AddDays(-1), Now));

        Assert.True(row.Counters.Measured);
        Assert.Equal(117, row.Counters.Dropped);
        Assert.Equal(741_375, row.Counters.Total);
        Assert.Equal(27, row.ScrambledPackets);
        Assert.Equal(2, row.Overflows);
        Assert.Equal(measured.Id, row.Recording);
        Assert.NotNull(row.MeasuredUpdatedAt);
    }

    [Fact(DisplayName = "a recording nothing counted comes back uncounted rather than as zeroes")]
    public async Task ARecordingNothingCountedComesBackUncounted()
    {
        await ClearAsync();
        await WrittenAsync(6021, Airs);

        QualityLedgerRow row = Assert.Single(await ReadAsync(Now.AddDays(-1), Now));

        Assert.False(row.Counters.Measured);
        Assert.Null(row.Counters.Dropped);
        Assert.Null(row.Counters.Total);
        Assert.Null(row.ScrambledPackets);
        Assert.Null(row.MeasuredUpdatedAt);
    }

    [Fact]
    public async Task AChannelTheCatalogueCarriesIsPlacedAndOneItDoesNotIsLeftUnplaced()
    {
        await ClearAsync();
        await WrittenAsync(6031, Airs, service: 1_024);
        await WrittenAsync(6032, Airs, service: 1_032);

        IReadOnlyList<QualityLedgerRow> read = await ReadAsync(
            Now.AddDays(-1),
            Now,
            new HeldStreams([Carried(1_024)]));

        Assert.Equal(TuneSystem.IsdbT, read.Single(row => row.Service.Value is 1_024).Kind);
        Assert.Null(read.Single(row => row.Service.Value is 1_032).Kind);
    }

    [Fact]
    public async Task ARecordingThatNamesNoTunerIsStillRead()
    {
        await ClearAsync();
        await WrittenAsync(6041, Airs, tuner: null);

        Assert.Null(Assert.Single(await ReadAsync(Now.AddDays(-1), Now)).Tuner);
    }

    private static BroadcastStream Carried(int service)
        => new(
            new NetworkId(32_736),
            new TransportStreamId(32_736),
            TuningParameters.Terrestrial(27),
            [new ServiceId(service)]);

    private async Task<IReadOnlyList<QualityLedgerRow>> ReadAsync(
        DateTime from,
        DateTime until,
        IBroadcastStreamDirectory? streams = null)
    {
        await using CarinaDbContext reading = database.Open();

        return await new QualityLedgerReader(reading, streams ?? new HeldStreams([]))
            .ReadAsync(QualityPeriod.Of(from, until, Now)!, Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<Recording>().ExecuteDeleteAsync(Cancel);
    }

    private async Task<Recording> WrittenAsync(
        int eventId,
        DateTime airs,
        long? dropped = null,
        long? total = null,
        long? scrambled = null,
        long overflows = 0,
        int service = 1_024,
        string? tuner = "adapter3.frontend0")
    {
        RecordingId id = RecordingId.New();
        Recording begun = Recording.Begin(
            id,
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(32_736), new ServiceId(service), new EventId(eventId), airs),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".ts"),
            airs,
            airs.AddMinutes(30),
            new ProgrammeSnapshot("A programme", string.Empty, string.Empty, [], airs.AddHours(-6)),
            null,
            BroadcastGroupRole.Standalone,
            airs,
            tuner is null ? null : new TunerDeviceId(tuner));

        await using CarinaDbContext writing = database.Open();
        var repository = new RecordingRepository(writing);
        await repository.AddAsync(begun, Cancel);

        if (dropped is { } lost && total is { } carried)
        {
            begun.Measure(
                DropCounters.Counted(lost, carried),
                DropTimeline.Unlocated,
                scrambled,
                overflows,
                airs.AddMinutes(10));
            await repository.SaveAsync(begun, Cancel);
        }

        return begun;
    }
}
