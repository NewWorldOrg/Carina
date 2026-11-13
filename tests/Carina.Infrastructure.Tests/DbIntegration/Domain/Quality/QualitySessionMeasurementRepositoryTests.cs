using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Puppis.Contracts;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Quality;

[Trait("Category", "DbIntegration")]
public sealed class QualitySessionMeasurementRepositoryTests(MigratedRepositoryDatabase database)
    : IClassFixture<MigratedRepositoryDatabase>, IAsyncLifetime
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Microsecond = TimeSpan.FromTicks(10);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact(DisplayName = "a session measured again keeps one row and its latest counts")]
    public async Task ASessionMeasuredAgainKeepsOneRowAndItsLatestCounts()
    {
        await ClearAsync();

        await SaveAsync(Opened("survey-1"));

        await using (AsyncServiceScope moving = container.CreateAsyncScope())
        {
            IQualitySessionMeasurementRepository repository = Measurements(moving);

            QualitySessionMeasurement? found = await repository.FindAsync("instance-a", SessionId.Parse("survey-1"), Cancel);

            Assert.NotNull(found);

            found.Observe(12, 9012, 3, Noon.AddSeconds(10));

            await repository.SaveAsync(found, Cancel);
        }

        QualitySessionMeasurement later = Opened("survey-1");
        later.Observe(20, 18020, 4, Noon.AddSeconds(20));

        await SaveAsync(later);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        QualitySessionMeasurement kept = Assert.Single(
            await Measurements(reading).ListStartedBetweenAsync(Noon.AddHours(-1), Noon.AddHours(1), Cancel));

        Assert.True(kept.CcMeasured);
        Assert.Equal(20, kept.CcDroppedPackets);
        Assert.Equal(18020, kept.CcTotalPackets);
        Assert.Equal(4, kept.EovfCount);
        Assert.Equal(Noon.AddSeconds(20), kept.MeasuredUpdatedAt);
    }

    [Fact(DisplayName = "only the sessions not yet closed are listed as open")]
    public async Task OnlyTheSessionsNotYetClosedAreListedAsOpen()
    {
        await ClearAsync();

        QualitySessionMeasurement closed = Opened("survey-1");
        closed.Close(Noon.AddMinutes(1));

        await SaveAsync(closed);
        await SaveAsync(Opened("live-2", SessionPurpose.Live));

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        QualitySessionMeasurement open = Assert.Single(await Measurements(reading).ListOpenAsync(Cancel));

        Assert.Equal("live-2", open.Session.Value);
    }

    [Fact(DisplayName = "BR-QD-005: the session measurements the store reads as open are the ones each says has not ended, closed on its start or a microsecond after among them")]
    public async Task TheMeasurementsReadAsOpenAreTheOnesThatHaveNotEnded()
    {
        await ClearAsync();

        List<QualitySessionMeasurement> laid =
        [
            Measurement("open-1", null),
            Measurement("open-2", null, measured: true),
            Measurement("closed-at-start", Noon.AddMinutes(-1)),
            Measurement("closed-a-microsecond-on", Noon.Add(Microsecond)),
            Measurement("closed-later", Noon.AddHours(1)),
        ];

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            CarinaDbContext context = writing.ServiceProvider.GetRequiredService<CarinaDbContext>();
            context.AddRange(laid);
            await context.SaveChangesAsync(Cancel);
        }

        IReadOnlyList<QualitySessionMeasurement> stored = await ReadAllAsync();

        Assert.Contains(stored, measurement => measurement.HasEnded && measurement.EndedAt == measurement.StartedAt);
        Assert.Contains(stored, measurement => !measurement.HasEnded);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Equal(
            Sessions(stored.Where(measurement => !measurement.HasEnded)),
            Sessions(await Measurements(reading).ListOpenAsync(Cancel)));
    }

    private static IQualitySessionMeasurementRepository Measurements(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IQualitySessionMeasurementRepository>();

    private static QualitySessionMeasurement Opened(string session, SessionPurpose purpose = SessionPurpose.Survey)
        => QualitySessionMeasurement.Open(
            "instance-a",
            SessionId.Parse(session),
            purpose,
            new TunerDeviceId("adapter3.frontend0"),
            new NetworkId(32736),
            new ServiceId(1024),
            Noon);

    private static QualitySessionMeasurement Measurement(string session, DateTime? closedAt, bool measured = false)
    {
        QualitySessionMeasurement measurement = QualitySessionMeasurement.Open(
            "instance-a",
            SessionId.Parse(session),
            SessionPurpose.Live,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            Noon);

        if (measured)
        {
            measurement.Observe(2, 9000, 0, Noon.AddMinutes(1));
        }

        if (closedAt is { } at)
        {
            measurement.Close(at);
        }

        return measurement;
    }

    private static string[] Sessions(IEnumerable<QualitySessionMeasurement> measurements)
        => [.. measurements.Select(measurement => measurement.Session.ToString()).Order(StringComparer.Ordinal)];

    private async Task SaveAsync(QualitySessionMeasurement measurement)
    {
        await using AsyncServiceScope writing = container.CreateAsyncScope();

        await Measurements(writing).SaveAsync(measurement, Cancel);
    }

    private async Task<IReadOnlyList<QualitySessionMeasurement>> ReadAllAsync()
    {
        await using AsyncServiceScope reading = container.CreateAsyncScope();

        return await reading.ServiceProvider.GetRequiredService<CarinaDbContext>()
            .Set<QualitySessionMeasurement>()
            .AsNoTracking()
            .ToListAsync(Cancel);
    }

    private async Task ClearAsync()
    {
        await using AsyncServiceScope clearing = container.CreateAsyncScope();
        await clearing.ServiceProvider.GetRequiredService<CarinaDbContext>()
            .Set<QualitySessionMeasurement>()
            .ExecuteDeleteAsync(Cancel);
    }
}
