using Carina.Domain.Quality;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Quality;

[Trait("Category", "DbIntegration")]
public sealed class QualityThresholdRepositoryTests(MigratedRepositoryDatabase database)
    : IClassFixture<MigratedRepositoryDatabase>, IAsyncLifetime
{
    private static readonly DateTime At = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact]
    public async Task ALevelSavedTwiceKeepsOneRowAndTheLatestValue()
    {
        await ClearAsync();

        await SaveAsync(QualityThresholdKey.PacketsLostWarning, 0.0002);
        await SaveAsync(QualityThresholdKey.PacketsLostWarning, 0.0005);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IQualityThresholdRepository repository = Thresholds(reading);

        QualityThreshold held = Assert.Single(await repository.ListAsync(Cancel));

        Assert.Equal(0.0005, held.Setting.Current);
        Assert.Equal(0.0002, held.Setting.Default);
        Assert.NotNull(await repository.FindAsync(QualityThresholdKey.PacketsLostWarning, Cancel));
        Assert.Null(await repository.FindAsync(QualityThresholdKey.LockRate, Cancel));
    }

    [Fact(DisplayName = "a level read and then written in the same breath is written, not refused for being read")]
    public async Task ALevelReadAndThenWrittenInTheSameBreathIsWritten()
    {
        await ClearAsync();
        await SaveAsync(QualityThresholdKey.PacketsLostWarning, 0.0002);

        await using (AsyncServiceScope both = container.CreateAsyncScope())
        {
            IQualityThresholdRepository repository = Thresholds(both);

            await repository.ListAsync(Cancel);

            await repository.SaveAsync(
                QualityThreshold.Rehydrate(
                    QualityThresholdKey.PacketsLostWarning,
                    Threshold.Of(0.0002, 0.0007, provisional: true, 0, At),
                    null),
                Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        QualityThreshold held = Assert.Single(await Thresholds(reading).ListAsync(Cancel));

        Assert.Equal(0.0007, held.Setting.Current);
    }

    [Fact(DisplayName = "BR-QD-023: a measured level keeps the measurement it stands on, and a level set by hand says so")]
    public async Task AMeasuredLevelKeepsTheMeasurementItStandsOn()
    {
        await ClearAsync();

        QualityThresholdMeasurement measurement =
            QualityThresholdMeasurement.Of(19_500, 240, 18, At.AddDays(-7), At, At.AddMinutes(5));

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IQualityThresholdRepository repository = Thresholds(writing);

            await repository.SaveAsync(
                QualityThreshold.Rehydrate(
                    QualityThresholdKey.CarrierToNoiseFloor,
                    Threshold.Of(15_000, 19_500, provisional: false, 240, At),
                    null,
                    byHand: false,
                    measurement),
                Cancel);
            await repository.SaveAsync(
                QualityThreshold.Rehydrate(
                    QualityThresholdKey.BitErrorRateCeiling,
                    Threshold.Of(0.0001, 0.003, provisional: true, 0, At),
                    null,
                    byHand: true,
                    null),
                Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<QualityThreshold> held = await Thresholds(reading).ListAsync(Cancel);
        QualityThreshold measured = held.Single(threshold => threshold.Key == QualityThresholdKey.CarrierToNoiseFloor);
        QualityThreshold byHand = held.Single(threshold => threshold.Key == QualityThresholdKey.BitErrorRateCeiling);

        Assert.Equal(measurement, measured.Measurement);
        Assert.False(measured.ByHand);
        Assert.False(measured.Setting.Provisional);
        Assert.True(byHand.ByHand);
        Assert.Null(byHand.Measurement);
    }

    [Fact(DisplayName = "BR-QD-023: a writer of the levels holds its turn until its transaction ends, and the next one waits for it")]
    public async Task AWriterOfTheLevelsHoldsItsTurnUntilItsTransactionEnds()
    {
        await using AsyncServiceScope first = container.CreateAsyncScope();
        await using AsyncServiceScope second = container.CreateAsyncScope();
        await using IDbContextTransaction holding =
            await Context(first).Database.BeginTransactionAsync(Cancel);

        await Thresholds(first).TakeTurnAsync(Cancel);

        await using IDbContextTransaction waiting =
            await Context(second).Database.BeginTransactionAsync(Cancel);
        Task next = Thresholds(second).TakeTurnAsync(Cancel);

        Assert.NotSame(next, await Task.WhenAny(next, Task.Delay(TimeSpan.FromMilliseconds(300), Cancel)));

        await holding.CommitAsync(Cancel);
        await next.WaitAsync(TimeSpan.FromSeconds(10), Cancel);
        await waiting.CommitAsync(Cancel);
    }

    private static IQualityThresholdRepository Thresholds(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IQualityThresholdRepository>();

    private static CarinaDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();

    private async Task SaveAsync(QualityThresholdKey key, double current)
    {
        await using AsyncServiceScope writing = container.CreateAsyncScope();

        await Thresholds(writing).SaveAsync(
            QualityThreshold.Rehydrate(key, Threshold.Of(0.0002, current, provisional: true, 0, At), null),
            Cancel);
    }

    private async Task ClearAsync()
    {
        await using AsyncServiceScope clearing = container.CreateAsyncScope();
        await Context(clearing).Set<QualityThreshold>().ExecuteDeleteAsync(Cancel);
    }
}
