using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Quality;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class QualitySessionMeasurementStoreTests(RepositoryDatabase database)
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "決定4: a session measured again keeps one row and its latest counts")]
    public async Task ASessionMeasuredAgainKeepsOneRowAndItsLatestCounts()
    {
        await ClearAsync();

        await SaveAsync(Opened("survey-1"));

        await using (CarinaDbContext moving = database.Open())
        {
            var repository = new QualitySessionMeasurementRepository(moving);

            QualitySessionMeasurement? found = await repository.FindAsync("instance-a", SessionId.Parse("survey-1"), Cancel);

            Assert.NotNull(found);

            found.Observe(12, 9012, 3, Noon.AddSeconds(10));

            await repository.SaveAsync(found, Cancel);
        }

        QualitySessionMeasurement later = Opened("survey-1");
        later.Observe(20, 18020, 4, Noon.AddSeconds(20));

        await SaveAsync(later);

        await using CarinaDbContext reading = database.Open();

        QualitySessionMeasurement kept = Assert.Single(
            await new QualitySessionMeasurementRepository(reading)
                .ListStartedBetweenAsync(Noon.AddHours(-1), Noon.AddHours(1), Cancel));

        Assert.True(kept.CcMeasured);
        Assert.Equal(20, kept.CcDroppedPackets);
        Assert.Equal(18020, kept.CcTotalPackets);
        Assert.Equal(4, kept.EovfCount);
        Assert.Equal(Noon.AddSeconds(20), kept.MeasuredUpdatedAt);
    }

    [Fact(DisplayName = "BR-QD-005: only the sessions not yet closed are listed as open")]
    public async Task OnlyTheSessionsNotYetClosedAreListedAsOpen()
    {
        await ClearAsync();

        QualitySessionMeasurement closed = Opened("survey-1");
        closed.Close(Noon.AddMinutes(1));

        await SaveAsync(closed);
        await SaveAsync(Opened("live-2", SessionPurpose.Live));

        await using CarinaDbContext reading = database.Open();

        QualitySessionMeasurement open = Assert.Single(
            await new QualitySessionMeasurementRepository(reading).ListOpenAsync(Cancel));

        Assert.Equal("live-2", open.Session.Value);
    }

    private static QualitySessionMeasurement Opened(string session, SessionPurpose purpose = SessionPurpose.Survey)
        => QualitySessionMeasurement.Open(
            "instance-a",
            SessionId.Parse(session),
            purpose,
            new TunerDeviceId("adapter3.frontend0"),
            new NetworkId(32736),
            new ServiceId(1024),
            Noon);

    private async Task SaveAsync(QualitySessionMeasurement measurement)
    {
        await using CarinaDbContext writing = database.Open();

        await new QualitySessionMeasurementRepository(writing).SaveAsync(measurement, Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<QualitySessionMeasurement>().ExecuteDeleteAsync(Cancel);
    }
}
