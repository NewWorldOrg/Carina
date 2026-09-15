using Carina.Api.Common;
using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Integrity;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Api.Tests.Unit;

public sealed class IntegrityServiceTests
{
    private static readonly OutputRoot Root = new("primary");

    private static readonly DateTime LastWrittenAt = new(2026, 9, 10, 2, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime NoticedAt = new(2026, 9, 10, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime ThrownAt = new(2026, 9, 10, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AClientThatCancelsRightAfterTheEraseCompletesStillGetsTheFindingThrownAway()
    {
        IntegrityCheckId checkId = IntegrityCheckId.New();
        IntegrityCheck check = IntegrityCheck.Rehydrate(checkId, NoticedAt, NoticedAt, 0, 0, 0, 0, 0, 0, 0);
        IntegrityFinding finding = IntegrityFinding.NoLedgerRow(
            checkId,
            Root,
            "stray.m2ts",
            100,
            NoticedAt,
            LastWrittenAt);

        using var callerGaveUp = new CancellationTokenSource();
        var checks = new ThrowsWhenTheCallerHasGivenUp(check, finding);
        using IntegrityCheckJob sweeps = Sweeps();

        var service = new IntegrityService(
            sweeps,
            checks,
            new NoLedgerRows(),
            new NoDeclaredFiles(),
            new EraserThatCancelsTheCaller(callerGaveUp),
            new FindingDisposals(),
            new IntegritySettings(),
            new FixedTimeProvider(ThrownAt),
            NullLogger<IntegrityService>.Instance);

        ServiceResult<FindingThrownAway, FindingDisposalFailure> result =
            await service.ThrowAwayAsync(finding.Id, callerGaveUp.Token);

        Assert.True(result.IsSuccess);
        Assert.NotNull(finding.ThrownAwayAt);
        Assert.Equal(ThrownAt, finding.ThrownAwayAt);
    }

    private static IntegrityCheckJob Sweeps()
    {
        IServiceScopeFactory scopes = new ServiceCollection().BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new IntegrityCheckJob(
            scopes,
            new SurveyNeverAskedInTheseTests(),
            new IntegritySettings(),
            new FixedTimeProvider(ThrownAt),
            NullLogger<IntegrityCheckJob>.Instance);
    }

    private sealed class SurveyNeverAskedInTheseTests : IRecordingFileSurvey
    {
        public Task<IReadOnlyList<OutputRoot>> RootsAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<RootListing> ListAsync(OutputRoot root, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class NoLedgerRows : IRecordingLedger
    {
        public Task<IReadOnlyList<LedgerFile>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LedgerFile>>([]);
    }

    private sealed class NoDeclaredFiles : IEncodeWorkLedger
    {
        public Task<IReadOnlyList<DeclaredFile>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DeclaredFile>>([]);
    }

    private sealed class EraserThatCancelsTheCaller(CancellationTokenSource caller) : IStrayFileEraser
    {
        public Task<StrayFileErasure> EraseAsync(IntegrityFinding finding, CancellationToken cancellationToken)
        {
            caller.Cancel();

            return Task.FromResult(StrayFileErasure.Erased(fileRemoved: true));
        }
    }

    private sealed class ThrowsWhenTheCallerHasGivenUp(IntegrityCheck check, IntegrityFinding finding)
        : IIntegrityCheckRepository
    {
        public Task SaveAsync(IntegrityReport report, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IntegrityCheck?> LatestAsync(CancellationToken cancellationToken)
            => Task.FromResult<IntegrityCheck?>(check);

        public Task<PaginatedList<IntegrityFinding>> ListFindingsAsync(
            IntegrityCheckId checkId,
            IntegrityFindingQuery query,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IntegrityFinding?> FindFindingAsync(
            IntegrityCheckId checkId,
            IntegrityFindingId findingId,
            CancellationToken cancellationToken)
            => Task.FromResult(checkId.Equals(check.Id) && findingId.Equals(finding.Id) ? finding : null);

        public Task<bool> ThrowAwayFindingAsync(
            IntegrityCheckId checkId,
            IntegrityFindingId findingId,
            DateTime at,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            finding.ThrowAway(at);

            return Task.FromResult(true);
        }
    }
}
