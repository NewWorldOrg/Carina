using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Quality;
using Carina.TestSupport;

namespace Carina.Api.Tests.Unit;

public sealed class QualityThresholdServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ALevelWhoseChangeCouldNotBeRecordedIsNotMovedEither()
    {
        var thresholds = new HeldQualityThresholds();
        var service = new QualityThresholdService(
            thresholds,
            new RefusedChanges(),
            new UndoneWhenThrown(thresholds),
            new SilentEvents(),
            new HandTurnedClock(new DateTimeOffset(Now)));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            service.ReviseAsync(QualityThresholdKey.PacketsLostWarning, 0.0005, CancellationToken.None));

        Assert.Empty(thresholds.Thresholds);
    }

    private sealed class RefusedChanges : IQualityThresholdChangeRepository
    {
        public Task AddAsync(QualityThresholdChange change, CancellationToken cancellationToken)
            => throw new TimeoutException("the ledger did not answer");

        public Task<IReadOnlyList<QualityThresholdChange>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<QualityThresholdChange>>([]);
    }

    private sealed class UndoneWhenThrown(HeldQualityThresholds thresholds) : IAtomicWrite
    {
        public async Task<T> AllOrNothingAsync<T>(Func<CancellationToken, Task<T>> write, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(write);

            QualityThreshold[] before = [.. thresholds.Thresholds];

            try
            {
                return await write(cancellationToken);
            }
            catch
            {
                thresholds.Thresholds.Clear();
                thresholds.Thresholds.AddRange(before);

                throw;
            }
        }
    }
}
