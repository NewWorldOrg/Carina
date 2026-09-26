using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Encodings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class StationWatermarkRepositoryTests(RepositoryDatabase database)
{
    private static readonly NetworkId Network = new(32741);

    private static readonly ServiceId Service = new(1040);

    private static readonly ServiceId Neighbour = new(1041);

    private static readonly DateTime First = new(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "a recording is found the watermark learned most recently from another recording of its service, never the one learned from itself")]
    public async Task ARecordingIsFoundTheWatermarkLearnedAheadNeverItsOwn()
    {
        await ClearAsync();
        RecordingId earlier = RecordingId.New();
        RecordingId judged = RecordingId.New();
        await KeepAsync(Learned(Service, earlier, First, 3));
        await KeepAsync(Learned(Service, judged, First.AddHours(1), 4));
        await KeepAsync(Learned(Neighbour, RecordingId.New(), First.AddHours(2), 5));

        await using CarinaDbContext reading = database.Open();
        var watermarks = new StationWatermarkRepository(reading);

        StationWatermark? ahead = await watermarks.FindAheadOfAsync(Network, Service, judged, Cancel);
        StationWatermark? forAnother = await watermarks.FindAheadOfAsync(Network, Service, RecordingId.New(), Cancel);
        StationWatermark? forTheFirst = await watermarks.FindAheadOfAsync(Network, new ServiceId(1049), RecordingId.New(), Cancel);

        Assert.NotNull(ahead);
        Assert.Equal(earlier, ahead.LearnedFrom);
        Assert.Equal(Learned(Service, earlier, First, 3).Pattern, ahead.Pattern);
        Assert.NotNull(forAnother);
        Assert.Equal(judged, forAnother.LearnedFrom);
        Assert.Null(forTheFirst);
    }

    [Fact(DisplayName = "a watermark learned again from the same recording replaces the one it learned before")]
    public async Task AWatermarkLearnedAgainFromTheSameRecordingReplacesTheOneBefore()
    {
        await ClearAsync();
        RecordingId from = RecordingId.New();
        await KeepAsync(Learned(Service, from, First, 3));
        await KeepAsync(Learned(Service, from, First.AddHours(1), 6));

        await using CarinaDbContext reading = database.Open();
        StationWatermark kept = Assert.Single(await reading.Set<StationWatermark>().ToListAsync(Cancel));

        Assert.Equal(First.AddHours(1), kept.LearnedAt);
        Assert.Equal(Learned(Service, from, First, 6).Pattern, kept.Pattern);
    }

    [Fact(DisplayName = "a service keeps only its most recent watermarks, and another service's are left alone")]
    public async Task AServiceKeepsOnlyItsMostRecentWatermarks()
    {
        await ClearAsync();
        RecordingId neighbours = RecordingId.New();
        await KeepAsync(Learned(Neighbour, neighbours, First, 3));

        List<RecordingId> taught = [];

        for (int hour = 0; hour < StationWatermark.KeptPerService + 2; hour++)
        {
            RecordingId from = RecordingId.New();
            taught.Add(from);
            await KeepAsync(Learned(Service, from, First.AddHours(hour + 1), 3));
        }

        await using CarinaDbContext reading = database.Open();
        List<StationWatermark> kept = await reading.Set<StationWatermark>().ToListAsync(Cancel);

        Assert.Equal(
            taught.TakeLast(StationWatermark.KeptPerService).Select(id => id.Value).Order().ToArray(),
            kept.Where(watermark => watermark.ServiceId.Equals(Service)).Select(watermark => watermark.LearnedFrom.Value).Order().ToArray());
        Assert.Equal(neighbours, Assert.Single(kept, watermark => watermark.ServiceId.Equals(Neighbour)).LearnedFrom);
    }

    private static StationWatermark Learned(ServiceId service, RecordingId from, DateTime at, int pixels)
        => StationWatermark.Learn(Network, service, from, WatermarkMask.Covering([.. Enumerable.Range(0, pixels)]), at);

    private async Task KeepAsync(StationWatermark learned)
    {
        await using CarinaDbContext writing = database.Open();
        await new StationWatermarkRepository(writing).KeepAsync(learned, Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<StationWatermark>().ExecuteDeleteAsync(Cancel);
    }
}
