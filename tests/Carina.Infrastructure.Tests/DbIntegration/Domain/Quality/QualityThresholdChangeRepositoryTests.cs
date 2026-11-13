using Carina.Domain.Quality;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Quality;

[Trait("Category", "DbIntegration")]
public sealed class QualityThresholdChangeRepositoryTests(MigratedRepositoryDatabase database)
    : IClassFixture<MigratedRepositoryDatabase>, IAsyncLifetime
{
    private static readonly DateTime At = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact(DisplayName = "every change is kept, newest first, so the latest under a key is the first one there")]
    public async Task EveryChangeIsKeptNewestFirst()
    {
        await ClearAsync();

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IQualityThresholdChangeRepository history = Changes(writing);
            await history.AddAsync(Change(QualityThresholdKey.PacketsLostWarning, 0.0002, 0.0005, At), Cancel);
            await history.AddAsync(Change(QualityThresholdKey.PacketsLostWarning, 0.0005, 0.0009, At.AddHours(1)), Cancel);
            await history.AddAsync(Change(QualityThresholdKey.LockRate, 0.99, 0.95, At.AddMinutes(30)), Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        IReadOnlyList<QualityThresholdChange> all = await Changes(reading).ListAsync(Cancel);
        QualityThresholdChange latest = all.First(change => change.Key == QualityThresholdKey.PacketsLostWarning);

        Assert.Equal(3, all.Count);
        Assert.Equal(At.AddHours(1), all[0].ChangedAt);
        Assert.Equal(0.0005, latest.PreviousValue);
        Assert.Equal(0.0009, latest.NextValue);
        Assert.DoesNotContain(all, change => change.Key == QualityThresholdKey.Overflows);
    }

    [Fact(DisplayName = "BR-QV-002: a change keeps whether a hand or a measurement made it")]
    public async Task AChangeKeepsWhetherAHandOrAMeasurementMadeIt()
    {
        await ClearAsync();

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Changes(writing).AddAsync(
                QualityThresholdChange.Record(
                    QualityThresholdChangeId.New(),
                    QualityThresholdKey.CarrierToNoiseFloor,
                    15_000,
                    19_500,
                    At,
                    null,
                    QualityThresholdChangeCause.Measurement),
                Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Equal(
            QualityThresholdChangeCause.Measurement,
            Assert.Single(await Changes(reading).ListAsync(Cancel)).Cause);
    }

    private static IQualityThresholdChangeRepository Changes(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IQualityThresholdChangeRepository>();

    private static QualityThresholdChange Change(QualityThresholdKey key, double previous, double next, DateTime at)
        => QualityThresholdChange.Record(QualityThresholdChangeId.New(), key, previous, next, at, null);

    private async Task ClearAsync()
    {
        await using AsyncServiceScope clearing = container.CreateAsyncScope();
        await clearing.ServiceProvider.GetRequiredService<CarinaDbContext>()
            .Set<QualityThresholdChange>()
            .ExecuteDeleteAsync(Cancel);
    }
}
