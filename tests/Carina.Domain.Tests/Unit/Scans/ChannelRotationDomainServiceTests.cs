using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Scans;

[Trait("Category", "Unit")]
public sealed class ChannelRotationDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

    private static readonly NetworkId Network = new(4);

    private static readonly RotationBackoff Backoff = new(TimeSpan.FromMinutes(1), 2, TimeSpan.FromHours(1), 2);

    private readonly HeldServices services = new();

    private readonly HeldCandidates candidates = new();

    private ChannelRotationDomainService Rotation => new(services, candidates);

    [Fact(DisplayName = "BR-TS-002: a candidate whose channel reached a stream naming its service is received, with what was measured")]
    public async Task ACandidateReachedIsReceived()
    {
        CandidateChannel candidate = Held(101, TuningParameters.Terrestrial(27));
        candidate.RecordTuningFailure(Backoff, At.AddHours(-1));
        SignalMeasurement measured = SignalMeasurement.WithLock(At, 21_000);

        IReadOnlyList<RotationDeparture> departures = await Rotation.RecordScanAsync(
            [Attempt(TuningParameters.Terrestrial(27), ScanAttemptOutcome.Succeeded, measured)],
            Naming(TuningParameters.Terrestrial(27), 101),
            Backoff,
            At,
            Cancel);

        Assert.Empty(departures);
        Assert.Equal(0, candidate.ConsecutiveFailures);
        Assert.Same(measured, candidate.LastMeasurement);
        Assert.Equal(1, candidates.Saves);
    }

    [Fact(DisplayName = "BR-TS-002: a candidate received with nothing measured is taken as locked when the scan ended")]
    public async Task ACandidateReceivedUnmeasuredIsTakenAsLocked()
    {
        CandidateChannel candidate = Held(101, TuningParameters.Terrestrial(27));

        await Rotation.RecordScanAsync(
            [Attempt(TuningParameters.Terrestrial(27), ScanAttemptOutcome.Succeeded)],
            Naming(TuningParameters.Terrestrial(27), 101),
            Backoff,
            At,
            Cancel);

        Assert.Equal(SignalMeasurement.WithLock(At), candidate.LastMeasurement);
    }

    [Fact(DisplayName = "BR-TS-002: a candidate whose channel reached a stream not naming its service, or did not lock, fails")]
    public async Task ACandidateNotReachedFails()
    {
        CandidateChannel unnamed = Held(101, TuningParameters.Terrestrial(27));
        CandidateChannel unlocked = Held(102, TuningParameters.Terrestrial(28));

        await Rotation.RecordScanAsync(
            [
                Attempt(TuningParameters.Terrestrial(27), ScanAttemptOutcome.Succeeded),
                Attempt(TuningParameters.Terrestrial(28), ScanAttemptOutcome.NoLock),
            ],
            Naming(TuningParameters.Terrestrial(27), 103),
            Backoff,
            At,
            Cancel);

        Assert.Equal(1, unnamed.ConsecutiveFailures);
        Assert.Equal(1, unlocked.ConsecutiveFailures);
        Assert.Equal(2, candidates.Saves);
    }

    [Fact(DisplayName = "BR-TD-004: an attempt that says nothing of reception, or a channel not walked, leaves the candidate alone")]
    public async Task WhatSaysNothingOfReceptionLeavesTheCandidateAlone()
    {
        CandidateChannel incomplete = Held(101, TuningParameters.Terrestrial(27));
        CandidateChannel unwalked = Held(102, TuningParameters.Terrestrial(28));

        await Rotation.RecordScanAsync(
            [Attempt(TuningParameters.Terrestrial(27), ScanAttemptOutcome.IncompleteTables)],
            Naming(TuningParameters.Terrestrial(27), 101),
            Backoff,
            At,
            Cancel);

        Assert.Equal(0, incomplete.ConsecutiveFailures);
        Assert.Equal(0, unwalked.ConsecutiveFailures);
        Assert.Equal(0, candidates.Saves);
    }

    [Fact(DisplayName = "BR-TS-002: a candidate the failure takes out of the rotation is a departure, and one already out is not again")]
    public async Task ACandidateTakenOutOfTheRotationIsADeparture()
    {
        CandidateChannel leaving = Held(101, TuningParameters.Terrestrial(27));
        leaving.RecordTuningFailure(Backoff, At.AddHours(-1));
        CandidateChannel gone = Held(102, TuningParameters.Terrestrial(28));
        gone.RecordTuningFailure(Backoff, At.AddHours(-2));
        gone.RecordTuningFailure(Backoff, At.AddHours(-1));

        IReadOnlyList<RotationDeparture> departures = await Rotation.RecordScanAsync(
            [
                Attempt(TuningParameters.Terrestrial(27), ScanAttemptOutcome.NoLock),
                Attempt(TuningParameters.Terrestrial(28), ScanAttemptOutcome.NoLock),
            ],
            new Dictionary<TuningParameters, IReadOnlySet<ServiceId>>(),
            Backoff,
            At,
            Cancel);

        RotationDeparture departure = Assert.Single(departures);
        Assert.Equal(new ServiceId(101), departure.ServiceId);
        Assert.Equal(TuningParameters.Terrestrial(27), departure.Tuning);
        Assert.Equal(2, departure.ConsecutiveFailures);
        Assert.Equal(At, departure.Since);
        Assert.False(leaving.IsInRotation);
    }

    [Fact(DisplayName = "BR-ES-002: a candidate a guide visit could not tune with counts the failure")]
    public async Task ACandidateAGuideVisitCouldNotTuneWithCountsTheFailure()
    {
        CandidateChannel candidate = Held(101, TuningParameters.Terrestrial(27));

        bool told = await Rotation.RecordTuneFailureAsync(candidate.Id, Backoff, At, Cancel);

        Assert.True(told);
        Assert.Equal(1, candidate.ConsecutiveFailures);
        Assert.Equal(RotationState.BackingOff, candidate.RotationState);
        Assert.Equal(At.AddMinutes(1), candidate.NextAttemptAt);
        Assert.Equal(1, candidates.Saves);
    }

    [Fact(DisplayName = "BR-ES-002: a candidate a guide visit reached is back in the rotation, keeping the signal a scan measured")]
    public async Task ACandidateAGuideVisitReachedIsBackInTheRotation()
    {
        CandidateChannel candidate = Held(101, TuningParameters.Terrestrial(27));
        candidate.RecordTuningSuccess(SignalMeasurement.WithLock(At.AddHours(-2), 36_000), At.AddHours(-2));
        candidate.RecordTuningFailure(Backoff, At.AddHours(-1));

        bool told = await Rotation.RecordTuneReachedAsync(candidate.Id, At, Cancel);

        Assert.True(told);
        Assert.Equal(0, candidate.ConsecutiveFailures);
        Assert.Equal(RotationState.Active, candidate.RotationState);
        Assert.Equal(At, candidate.LastSeenAt);
        Assert.Equal(36_000, candidate.LastMeasurement?.CnrMilliDecibels);
        Assert.Equal(1, candidates.Saves);
    }

    [Fact(DisplayName = "BR-ES-002: a candidate that is gone is told nothing")]
    public async Task ACandidateThatIsGoneIsToldNothing()
    {
        Assert.False(await Rotation.RecordTuneFailureAsync(CandidateChannelId.New(), Backoff, At, Cancel));
        Assert.False(await Rotation.RecordTuneReachedAsync(CandidateChannelId.New(), At, Cancel));
        Assert.Equal(0, candidates.Saves);
    }

    private CandidateChannel Held(int serviceId, TuningParameters tuning)
    {
        if (!services.Services.Any(service => service.ServiceId.Equals(new ServiceId(serviceId))))
        {
            services.Services.Add(BroadcastService.Discover(Network, new ServiceId(serviceId), "Fixture Service", ServiceCategory.Television, At));
        }

        CandidateChannel candidate = CandidateChannel.Discover(CandidateChannelId.New(), Network, new ServiceId(serviceId), tuning, At);
        candidates.Candidates.Add(candidate);

        return candidate;
    }

    private static Dictionary<TuningParameters, IReadOnlySet<ServiceId>> Naming(TuningParameters tuning, params int[] serviceIds)
        => new() { [tuning] = serviceIds.Select(id => new ServiceId(id)).ToHashSet() };

    private static ScanRunAttempt Attempt(
        TuningParameters tuning,
        ScanAttemptOutcome outcome,
        SignalMeasurement? measurement = null)
        => ScanRunAttempt.Rehydrate(
            ScanRunAttemptId.New(),
            ScanRunId.New(),
            tuning,
            outcome,
            measurement,
            null,
            null,
            At,
            At);
}
