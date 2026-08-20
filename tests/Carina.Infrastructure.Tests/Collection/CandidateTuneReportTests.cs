using Carina.Domain.Channels;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Scanning;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Collection;

public sealed class CandidateTuneReportTests
{
    private static readonly DateTime At = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

    private readonly HeldCandidates candidates = new();

    private CandidateTuneFailureReporter Reporter
        => new(candidates, ScanSettings.Default, NullLogger<CandidateTuneFailureReporter>.Instance);

    [Fact]
    public async Task ACollectionVisitDoesNotEraseTheCarrierToNoiseAScanRead()
    {
        CandidateChannel candidate = Held();
        candidate.RecordTuningSuccess(SignalMeasurement.WithLock(At, 36_000), At);

        await Reporter.ReportReachedAsync(candidate.Id, At.AddHours(1), CancellationToken.None);

        Assert.Equal(36_000, candidate.LastMeasurement?.CnrMilliDecibels);
        Assert.Equal(At, candidate.LastMeasurement?.MeasuredAt);
    }

    [Fact]
    public async Task ACollectionVisitStillSaysTheCandidateWasReachedJustNow()
    {
        CandidateChannel candidate = Held();
        candidate.RecordTuningSuccess(SignalMeasurement.WithLock(At, 36_000), At);
        candidate.RecordTuningFailure(RotationBackoff.Default, At);

        await Reporter.ReportReachedAsync(candidate.Id, At.AddHours(1), CancellationToken.None);

        Assert.Equal(At.AddHours(1), candidate.LastSeenAt);
        Assert.Equal(RotationState.Active, candidate.RotationState);
        Assert.Equal(0, candidate.ConsecutiveFailures);
    }

    private CandidateChannel Held()
    {
        var candidate = CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(1),
            new ServiceId(101),
            TuningParameters.Terrestrial(53),
            At);

        candidates.Candidates.Add(candidate);

        return candidate;
    }
}
