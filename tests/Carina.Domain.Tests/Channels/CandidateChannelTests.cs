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
    public void AFreshCandidateIsInRotationAndSelectedByNobody()
    {
        var candidate = Discovered();

        Assert.False(candidate.IsSelected);
        Assert.Null(candidate.SelectionSource);
        Assert.Null(candidate.SelectedAt);
        Assert.Null(candidate.SelectionMeasurement);
        Assert.Equal(RotationState.Active, candidate.RotationState);
        Assert.Equal(0, candidate.ConsecutiveFailures);
        Assert.True(candidate.IsInRotation);
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
        var candidate = Discovered();

        candidate.RecordTuningFailure(RotationBackoff.Default, At);
        var first = candidate.NextAttemptAt;

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
        var candidate = Discovered();

        for (var failure = 0; failure < 3; failure++)
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
        var candidate = Discovered();

        candidate.RecordTuningFailure(backoff, At);
        candidate.RecordTuningFailure(backoff, At.AddMinutes(5));
        candidate.RecordTuningFailure(backoff, At.AddMinutes(9));

        Assert.Equal(At.AddMinutes(5), candidate.NeedsAttentionSince);
    }

    [Fact]
    public void ASuccessfulTuneReturnsTheCandidateToRotation()
    {
        var backoff = new RotationBackoff(TimeSpan.FromMinutes(1), 2, TimeSpan.FromHours(1), 2);
        var candidate = Discovered();
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
    public void ManualConfirmationReturnsACandidateThatLeftRotation()
    {
        var backoff = new RotationBackoff(TimeSpan.FromMinutes(1), 2, TimeSpan.FromHours(1), 2);
        var candidate = Discovered();
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
        var candidate = Discovered();

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

    private static CandidateChannel Rehydrated(bool isSelected, SelectionSource? source)
        => CandidateChannel.Rehydrate(
            CandidateChannelId.New(),
            new NetworkId(4),
            new ServiceId(101),
            TuningParameters.Terrestrial(27),
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
