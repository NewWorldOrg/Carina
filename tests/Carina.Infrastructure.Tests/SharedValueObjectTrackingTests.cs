using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests;

public sealed class SharedValueObjectTrackingTests
{
    private static readonly DateTime At = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

    private static CarinaDbContext Carina()
    {
        var builder = new DbContextOptionsBuilder<CarinaDbContext>();
        builder.UseCarinaDatabase("Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder");

        return new CarinaDbContext(builder.Options);
    }

    [Fact]
    public void TwoCandidatesMayBeTunedByTheVerySameParameters()
    {
        using var context = Carina();
        var carrying = TuningParameters.Terrestrial(27);

        context.Attach(Candidate(1, carrying));

        context.Add(Candidate(2, carrying));

        Assert.Equal(2, context.ChangeTracker.Entries<CandidateChannel>().Count());
    }

    [Fact]
    public void TwoCandidatesMayCarryTheVerySameReading()
    {
        using var context = Carina();
        var measured = SignalMeasurement.WithLock(At, 21_000);
        var first = Candidate(1, TuningParameters.Terrestrial(27));
        var second = Candidate(2, TuningParameters.Terrestrial(27));
        first.RecordTuningSuccess(measured, At);
        second.RecordTuningSuccess(measured, At);
        context.Attach(first);

        context.Add(second);

        Assert.Equal(2, context.ChangeTracker.Entries<CandidateChannel>().Count());
    }

    [Fact]
    public void TwoAttemptsMayBeTunedByTheVerySameParametersAndCarryTheVerySameReading()
    {
        using var context = Carina();
        var carrying = TuningParameters.Terrestrial(27);
        var measured = SignalMeasurement.WithLock(At, 21_000);
        context.Attach(Attempt(carrying, measured));

        context.Add(Attempt(carrying, measured));

        Assert.Equal(2, context.ChangeTracker.Entries<ScanRunAttempt>().Count());
    }

    [Fact]
    public void OneReadingMayBeKeptByBothAnAttemptAndACandidateAtOnce()
    {
        using var context = Carina();
        var measured = SignalMeasurement.WithLock(At, 21_000);
        var candidate = Candidate(1, TuningParameters.Terrestrial(27));
        candidate.RecordTuningSuccess(measured, At);
        context.Attach(Attempt(TuningParameters.Terrestrial(27), measured));

        context.Add(candidate);

        Assert.Single(context.ChangeTracker.Entries<CandidateChannel>());
    }

    private static CandidateChannel Candidate(int service, TuningParameters carrying)
        => CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(1),
            new ServiceId(service),
            carrying,
            At);

    private static ScanRunAttempt Attempt(TuningParameters carrying, SignalMeasurement measured)
        => ScanRunAttempt.Rehydrate(
            ScanRunAttemptId.New(),
            ScanRunId.New(),
            carrying,
            ScanAttemptOutcome.Succeeded,
            measured,
            null,
            null,
            At,
            At);
}
