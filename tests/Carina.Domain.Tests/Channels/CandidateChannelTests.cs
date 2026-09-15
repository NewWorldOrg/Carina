using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class CandidateChannelTests
{
    private static readonly DateTime At = new(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);

    private static CandidateChannel Discovered()
        => CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(4),
            new ServiceId(101),
            TuningParameters.Terrestrial(27),
            At);

    [Fact]
    public void AFreshCandidateHasNotYetSeenWhichStreamCarriesIt()
    {
        CandidateChannel candidate = Discovered();

        Assert.Null(candidate.ObservedStreamId);
    }

    [Fact]
    public void ScanningTellsTheCandidateWhichStreamCarriesIt()
    {
        CandidateChannel candidate = Discovered();

        candidate.CarriedBy(new TransportStreamId(32_736));

        Assert.Equal(new TransportStreamId(32_736), candidate.ObservedStreamId);
    }

    [Fact]
    public void AStreamMayCorrectItselfWhenTheBroadcasterRenumbers()
    {
        CandidateChannel candidate = Discovered();

        candidate.CarriedBy(new TransportStreamId(32_736));
        candidate.CarriedBy(new TransportStreamId(32_737));

        Assert.Equal(new TransportStreamId(32_737), candidate.ObservedStreamId);
    }

    [Fact]
    public void AScanThatSawNoStreamLeavesTheKnownOneAlone()
    {
        CandidateChannel candidate = Discovered();

        candidate.CarriedBy(new TransportStreamId(32_736));
        candidate.CarriedBy(null);

        Assert.Equal(new TransportStreamId(32_736), candidate.ObservedStreamId);
    }

    [Fact]
    public void AFreshCandidateIsInRotationAndSelectedByNobody()
    {
        CandidateChannel candidate = Discovered();

        Assert.False(candidate.IsSelected);
        Assert.Null(candidate.SelectionSource);
        Assert.Null(candidate.SelectedAt);
        Assert.Null(candidate.SelectionMeasurement);
        Assert.Equal(RotationState.Active, candidate.RotationState);
        Assert.Equal(0, candidate.ConsecutiveFailures);
        Assert.True(candidate.IsInRotation);
    }

    [Fact]
    public void SelectingRecordsWhatChoseItAndWhatWasMeasuredAtThatMoment()
    {
        CandidateChannel candidate = Discovered();

        candidate.Select(SelectionSource.AutoSwitch, SignalMeasurement.WithLock(At, 20_500), At);

        Assert.True(candidate.IsSelected);
        Assert.Equal(SelectionSource.AutoSwitch, candidate.SelectionSource);
        Assert.Equal(At, candidate.SelectedAt);
        Assert.Equal(20_500, candidate.SelectionMeasurement?.CnrMilliDecibels);
    }

    [Fact]
    public void DroppingASelectionLeavesNothingBehindThatLooksLikeOne()
    {
        CandidateChannel candidate = Discovered();
        candidate.Select(SelectionSource.Manual, SignalMeasurement.WithLock(At, 20_500), At);

        candidate.Deselect();

        Assert.False(candidate.IsSelected);
        Assert.Null(candidate.SelectionSource);
        Assert.Null(candidate.SelectedAt);
        Assert.Null(candidate.SelectionMeasurement);
    }

    [Fact]
    public void NoPublicSetterOnACandidateCanTurnSelectionOn()
    {
        Assert.DoesNotContain(
            typeof(CandidateChannel).GetProperties(),
            property => property.Name == nameof(CandidateChannel.IsSelected)
                        && property.SetMethod?.IsPublic == true);
    }

    [Fact]
    public void AFreshCandidateHasNoScoreYet()
        => Assert.Null(Discovered().Score);

    [Fact(DisplayName = "BR-QD-012: writing a score back leaves the selection exactly where it was")]
    public void WritingAScoreBackLeavesTheSelectionExactlyWhereItWas()
    {
        CandidateChannel candidate = Discovered();
        SignalMeasurement atSelection = SignalMeasurement.WithLock(At, 20_500);
        candidate.Select(SelectionSource.Manual, atSelection, At);
        candidate.RecordTuningSuccess(SignalMeasurement.WithLock(At.AddHours(1), 21_000), At.AddHours(1));
        CandidateScore score = Scored(At.AddDays(1));

        candidate.Evaluated(score);

        Assert.Equal(score, candidate.Score);
        Assert.True(candidate.IsSelected);
        Assert.Equal(SelectionSource.Manual, candidate.SelectionSource);
        Assert.Equal(At, candidate.SelectedAt);
        Assert.Equal(atSelection, candidate.SelectionMeasurement);
        Assert.Equal(21_000, candidate.LastMeasurement?.CnrMilliDecibels);
        Assert.Equal(RotationState.Active, candidate.RotationState);
        Assert.Equal(0, candidate.ConsecutiveFailures);
        Assert.Equal(At.AddHours(1), candidate.LastSeenAt);
    }

    [Fact(DisplayName = "BR-QD-012: a score better than the selected one's does not select the candidate it belongs to")]
    public void AScoreDoesNotSelectTheCandidateItBelongsTo()
    {
        CandidateChannel candidate = Discovered();

        candidate.Evaluated(Scored(At.AddDays(1)));

        Assert.False(candidate.IsSelected);
        Assert.Null(candidate.SelectionSource);
        Assert.Null(candidate.SelectedAt);
    }

    [Fact]
    public void ALaterScoreTakesThePlaceOfTheEarlierOne()
    {
        CandidateChannel candidate = Discovered();

        candidate.Evaluated(Scored(At.AddDays(1)));
        candidate.Evaluated(Scored(At.AddDays(2)));

        Assert.Equal(At.AddDays(2), candidate.Score?.EvaluatedAt);
    }

    [Fact]
    public void ARehydratedCandidateKeepsTheScoreItWasStoredWith()
    {
        CandidateScore score = Scored(At.AddDays(1));

        CandidateChannel candidate = CandidateChannel.Rehydrate(
            CandidateChannelId.New(),
            new NetworkId(4),
            new ServiceId(101),
            TuningParameters.Terrestrial(27),
            null,
            false,
            null,
            null,
            null,
            null,
            false,
            RotationState.Active,
            0,
            null,
            null,
            At,
            At,
            score);

        Assert.Equal(score, candidate.Score);
    }

    [Fact]
    public void ARehydratedSelectionAlwaysNamesWhatSelectedIt()
    {
        Assert.Throws<ArgumentException>(() => Rehydrated(isSelected: true, source: null));
        Assert.Throws<ArgumentException>(() => Rehydrated(isSelected: false, source: SelectionSource.Manual));
    }

    [Fact]
    public void AnAutomaticSwitchIsToldApartFromAManualOne()
    {
        Assert.Equal(SelectionSource.AutoSwitch, Rehydrated(true, SelectionSource.AutoSwitch).SelectionSource);
        Assert.Equal(SelectionSource.Manual, Rehydrated(true, SelectionSource.Manual).SelectionSource);
    }

    [Fact]
    public void EachFailureBacksOffFurtherWithoutLeavingRotation()
    {
        CandidateChannel candidate = Discovered();

        candidate.RecordTuningFailure(RotationBackoff.Default, At);
        DateTime? first = candidate.NextAttemptAt;

        candidate.RecordTuningFailure(RotationBackoff.Default, At);

        Assert.Equal(RotationState.BackingOff, candidate.RotationState);
        Assert.Equal(2, candidate.ConsecutiveFailures);
        Assert.True(candidate.NextAttemptAt > first);
        Assert.True(candidate.IsInRotation);
        Assert.Null(candidate.NeedsAttentionSince);
    }

    [Fact]
    public void ReachingTheCeilingLeavesRotationAndSaysSinceWhen()
    {
        var backoff = new RotationBackoff(TimeSpan.FromMinutes(1), 2, TimeSpan.FromHours(1), 3);
        CandidateChannel candidate = Discovered();

        for (int failure = 0; failure < 3; failure++)
        {
            candidate.RecordTuningFailure(backoff, At.AddMinutes(failure));
        }

        Assert.Equal(RotationState.NeedsAttention, candidate.RotationState);
        Assert.False(candidate.IsInRotation);
        Assert.Equal(At.AddMinutes(2), candidate.NeedsAttentionSince);
        Assert.Null(candidate.NextAttemptAt);
    }

    [Fact]
    public void LeavingRotationKeepsTheHourItHappenedRatherThanMovingIt()
    {
        var backoff = new RotationBackoff(TimeSpan.FromMinutes(1), 2, TimeSpan.FromHours(1), 2);
        CandidateChannel candidate = Discovered();

        candidate.RecordTuningFailure(backoff, At);
        candidate.RecordTuningFailure(backoff, At.AddMinutes(5));
        candidate.RecordTuningFailure(backoff, At.AddMinutes(9));

        Assert.Equal(At.AddMinutes(5), candidate.NeedsAttentionSince);
    }

    [Fact]
    public void ASuccessfulTuneReturnsTheCandidateToRotation()
    {
        var backoff = new RotationBackoff(TimeSpan.FromMinutes(1), 2, TimeSpan.FromHours(1), 2);
        CandidateChannel candidate = Discovered();
        candidate.RequireRevalidation();
        candidate.RecordTuningFailure(backoff, At);
        candidate.RecordTuningFailure(backoff, At);

        candidate.RecordTuningSuccess(SignalMeasurement.WithLock(At, 21_000), At.AddHours(1));

        Assert.Equal(RotationState.Active, candidate.RotationState);
        Assert.Equal(0, candidate.ConsecutiveFailures);
        Assert.Null(candidate.NeedsAttentionSince);
        Assert.False(candidate.NeedsRevalidation);
        Assert.Equal(At.AddHours(1), candidate.LastSeenAt);
        Assert.Equal(21_000, candidate.LastMeasurement?.CnrMilliDecibels);
    }

    [Fact]
    public void ATuneThatCouldNotReadTheCarrierToNoiseKeepsTheReadingThatDid()
    {
        CandidateChannel candidate = Discovered();
        candidate.RecordTuningSuccess(SignalMeasurement.WithLock(At, 36_000), At);

        candidate.RecordTuningSuccess(SignalMeasurement.WithLock(At.AddHours(1)), At.AddHours(1));

        Assert.Equal(36_000, candidate.LastMeasurement?.CnrMilliDecibels);
        Assert.Equal(At, candidate.LastMeasurement?.MeasuredAt);
        Assert.Equal(At.AddHours(1), candidate.LastSeenAt);
    }

    [Fact]
    public void ATuneThatCouldNotReadTheCarrierToNoiseStillReturnsTheCandidateToRotation()
    {
        var backoff = new RotationBackoff(TimeSpan.FromMinutes(1), 2, TimeSpan.FromHours(1), 2);
        CandidateChannel candidate = Discovered();
        candidate.RecordTuningSuccess(SignalMeasurement.WithLock(At, 36_000), At);
        candidate.RecordTuningFailure(backoff, At);
        candidate.RecordTuningFailure(backoff, At);

        candidate.RecordTuningSuccess(SignalMeasurement.WithLock(At.AddHours(1)), At.AddHours(1));

        Assert.Equal(RotationState.Active, candidate.RotationState);
        Assert.Equal(0, candidate.ConsecutiveFailures);
        Assert.Equal(36_000, candidate.LastMeasurement?.CnrMilliDecibels);
    }

    [Fact]
    public void AWeakerReadingIsTakenOverTheStrongerOneItFollows()
    {
        CandidateChannel candidate = Discovered();
        candidate.RecordTuningSuccess(SignalMeasurement.WithLock(At, 36_000), At);

        candidate.RecordTuningSuccess(SignalMeasurement.WithLock(At.AddHours(1), 12_000), At.AddHours(1));

        Assert.Equal(12_000, candidate.LastMeasurement?.CnrMilliDecibels);
        Assert.Equal(At.AddHours(1), candidate.LastMeasurement?.MeasuredAt);
    }

    [Fact]
    public void ATuneThatDidNotLockDisplacesWhatWasReadWhileItDid()
    {
        CandidateChannel candidate = Discovered();
        candidate.RecordTuningSuccess(SignalMeasurement.WithLock(At, 36_000), At);

        candidate.RecordTuningSuccess(SignalMeasurement.WithoutLock(At.AddHours(1)), At.AddHours(1));

        Assert.False(candidate.LastMeasurement?.Locked);
        Assert.Null(candidate.LastMeasurement?.CnrMilliDecibels);
    }

    [Fact]
    public void AFirstReadingStandsEvenWhenTheCarrierToNoiseWasNotAmongIt()
    {
        CandidateChannel candidate = Discovered();

        candidate.RecordTuningSuccess(SignalMeasurement.WithLock(At), At);

        Assert.True(candidate.LastMeasurement?.Locked);
        Assert.Null(candidate.LastMeasurement?.CnrMilliDecibels);
        Assert.Equal(At, candidate.LastMeasurement?.MeasuredAt);
    }

    [Fact]
    public void ManualConfirmationReturnsACandidateThatLeftRotation()
    {
        var backoff = new RotationBackoff(TimeSpan.FromMinutes(1), 2, TimeSpan.FromHours(1), 2);
        CandidateChannel candidate = Discovered();
        candidate.RecordTuningFailure(backoff, At);
        candidate.RecordTuningFailure(backoff, At);

        candidate.ReturnToRotation(At.AddDays(1));

        Assert.Equal(RotationState.Active, candidate.RotationState);
        Assert.True(candidate.IsInRotation);
        Assert.Null(candidate.NeedsAttentionSince);
    }

    [Fact]
    public void AChangedTunerLedgerLeavesTheCandidateToBeRevalidatedRatherThanDeleted()
    {
        CandidateChannel candidate = Discovered();

        candidate.RequireRevalidation();

        Assert.True(candidate.NeedsRevalidation);
        Assert.Equal(RotationState.Active, candidate.RotationState);
    }

    [Fact]
    public void TimesArriveInUtcOrNotAtAll()
    {
        Assert.Throws<ArgumentException>(() => CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(4),
            new ServiceId(101),
            TuningParameters.Terrestrial(27),
            new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Local)));
    }

    private static CandidateScore Scored(DateTime evaluatedAt)
        => CandidateScore.Of(360, 350, 24_000, 0.0001, evaluatedAt.AddDays(-7), evaluatedAt, evaluatedAt);

    private static CandidateChannel Rehydrated(bool isSelected, SelectionSource? source)
        => CandidateChannel.Rehydrate(
            CandidateChannelId.New(),
            new NetworkId(4),
            new ServiceId(101),
            TuningParameters.Terrestrial(27),
            null,
            isSelected,
            source,
            isSelected ? At : null,
            isSelected ? SignalMeasurement.WithLock(At, 21_000) : null,
            null,
            false,
            RotationState.Active,
            0,
            null,
            null,
            At,
            At);
}
