using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Quality;

public sealed class CandidateScoringTests
{
    private static readonly DateTime At = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly QualityPeriod Week = QualityPeriod.Of(At.AddDays(-7), At, At)!;

    private static readonly NetworkId Network = new(4);

    [Fact(DisplayName = "a selected candidate is scored from the windows filed under its stream")]
    public void ASelectedCandidateIsScoredFromTheWindowsFiledUnderItsStream()
    {
        CandidateChannel candidate = Selected(101, 27, At.AddDays(-30));

        CandidateScored scored = Assert.Single(CandidateScoring.Over(
            [candidate],
            [Stream(27, 101)],
            [
                Window(At.AddHours(-3), 101, samples: 360, locked: 360, carrierToNoise: 24_000, errorRate: 0.0001),
                Window(At.AddHours(-2), 101, samples: 360, locked: 300, carrierToNoise: 21_000, errorRate: 0.0003),
            ],
            Week,
            At));

        Assert.Equal(candidate.Id, scored.Candidate);
        Assert.Equal(720, scored.Score.Samples);
        Assert.Equal(660.0 / 720, scored.Score.LockRate);
        Assert.Equal(21_000, scored.Score.CarrierToNoiseLowestMilliDecibels);
        Assert.Equal(0.0003, scored.Score.BitErrorRateHighest);
        Assert.Equal(Week.From, scored.Score.MeasuredFrom);
        Assert.Equal(Week.Until, scored.Score.MeasuredUntil);
        Assert.Equal(At, scored.Score.EvaluatedAt);
    }

    [Fact(DisplayName = "a candidate nothing was sampled on in the period is given no score")]
    public void ACandidateNothingWasSampledOnIsGivenNoScore()
        => Assert.Empty(CandidateScoring.Over(
            [Selected(101, 27, At.AddDays(-30))],
            [Stream(27, 101)],
            [],
            Week,
            At));

    [Fact(DisplayName = "samples that could not be read are not a score of nothing locked")]
    public void SamplesThatCouldNotBeReadAreNotAScoreOfNothingLocked()
        => Assert.Empty(CandidateScoring.Over(
            [Selected(101, 27, At.AddDays(-30))],
            [Stream(27, 101)],
            [Window(At.AddHours(-2), 101, samples: 360, locked: 0, unreachable: 360)],
            Week,
            At));

    [Fact]
    public void OnlyTheSamplesThatWereReadAreCountedBehindTheScore()
    {
        CandidateScored scored = Assert.Single(CandidateScoring.Over(
            [Selected(101, 27, At.AddDays(-30))],
            [Stream(27, 101)],
            [Window(At.AddHours(-2), 101, samples: 360, locked: 180, unreachable: 60)],
            Week,
            At));

        Assert.Equal(300, scored.Score.Samples);
        Assert.Equal(180.0 / 300, scored.Score.LockRate);
    }

    [Fact]
    public void ACandidateThatNeverLockedIsScoredAsHoldingNoLock()
    {
        CandidateScored scored = Assert.Single(CandidateScoring.Over(
            [Selected(101, 27, At.AddDays(-30))],
            [Stream(27, 101)],
            [Window(At.AddHours(-2), 101, samples: 360, locked: 0)],
            Week,
            At));

        Assert.Equal(0.0, scored.Score.LockRate);
        Assert.Null(scored.Score.CarrierToNoiseLowestMilliDecibels);
    }

    [Fact]
    public void WindowsFromBeforeTheSelectionAreNotCountedForIt()
    {
        DateTime selected = At.AddDays(-2);

        CandidateScored scored = Assert.Single(CandidateScoring.Over(
            [Selected(101, 27, selected)],
            [Stream(27, 101)],
            [
                Window(selected.AddHours(-1), 101, samples: 360, locked: 0),
                Window(selected.AddHours(1), 101, samples: 360, locked: 360),
            ],
            Week,
            At));

        Assert.Equal(360, scored.Score.Samples);
        Assert.Equal(1.0, scored.Score.LockRate);
        Assert.Equal(selected, scored.Score.MeasuredFrom);
    }

    [Fact]
    public void ALaterSelectionOnTheSameStreamMovesTheStartForEveryCandidateOnIt()
    {
        DateTime later = At.AddDays(-1);

        IReadOnlyList<CandidateScored> scored = CandidateScoring.Over(
            [Selected(101, 27, At.AddDays(-30)), Selected(102, 27, later)],
            [Stream(27, 101, 102)],
            [
                Window(later.AddHours(-1), 101, samples: 360, locked: 0),
                Window(later.AddHours(1), 101, samples: 360, locked: 360),
            ],
            Week,
            At);

        Assert.Equal(2, scored.Count);
        Assert.All(scored, each => Assert.Equal(later, each.Score.MeasuredFrom));
        Assert.All(scored, each => Assert.Equal(1.0, each.Score.LockRate));
    }

    [Fact]
    public void AnUnselectedCandidateOnTheSameChannelIsScoredFromTheSameReception()
    {
        CandidateChannel unselected = Candidate(102, 27);

        IReadOnlyList<CandidateScored> scored = CandidateScoring.Over(
            [Selected(101, 27, At.AddDays(-30)), unselected, Selected(102, 30, At.AddDays(-30))],
            [Stream(27, 101), Stream(30, 102)],
            [Window(At.AddHours(-2), 101, samples: 360, locked: 360)],
            Week,
            At);

        Assert.Contains(scored, each => each.Candidate.Equals(unselected.Id));
        Assert.False(unselected.IsSelected);
    }

    [Fact]
    public void OnlyTheCandidatesOnTheMeasuredChannelAreScored()
    {
        CandidateChannel onTheChannel = Selected(101, 27, At.AddDays(-30));
        CandidateChannel elsewhere = Candidate(101, 44);

        CandidateScored scored = Assert.Single(CandidateScoring.Over(
            [onTheChannel, elsewhere],
            [Stream(27, 101)],
            [Window(At.AddHours(-2), 101, samples: 360, locked: 360)],
            Week,
            At));

        Assert.Equal(onTheChannel.Id, scored.Candidate);
    }

    [Fact]
    public void AStreamWhoseSelectionsSitOnDifferentChannelsCannotSayWhichOneWasMeasured()
        => Assert.Empty(CandidateScoring.Over(
            [Selected(101, 27, At.AddDays(-30)), Selected(102, 30, At.AddDays(-30))],
            [Stream(27, 101, 102)],
            [Window(At.AddHours(-2), 101, samples: 360, locked: 360)],
            Week,
            At));

    [Fact]
    public void WindowsFiledUnderAnotherServiceOrNetworkAreNotCounted()
        => Assert.Empty(CandidateScoring.Over(
            [Selected(101, 27, At.AddDays(-30))],
            [Stream(27, 101)],
            [
                Window(At.AddHours(-2), 103, samples: 360, locked: 360),
                Window(At.AddHours(-2), 101, samples: 360, locked: 360) with { Network = new NetworkId(5) },
            ],
            Week,
            At));

    [Fact]
    public void WindowsOutsideThePeriodAreNotCounted()
        => Assert.Empty(CandidateScoring.Over(
            [Selected(101, 27, At.AddDays(-30))],
            [Stream(27, 101)],
            [
                Window(At.AddDays(-8), 101, samples: 360, locked: 360),
                Window(At, 101, samples: 360, locked: 360),
            ],
            Week,
            At));

    [Fact(DisplayName = "scoring selects nothing and deselects nothing")]
    public void ScoringSelectsNothingAndDeselectsNothing()
    {
        CandidateChannel selected = Selected(101, 27, At.AddDays(-30));
        CandidateChannel unselected = Candidate(101, 44);

        CandidateScoring.Over(
            [selected, unselected],
            [Stream(27, 101)],
            [Window(At.AddHours(-2), 101, samples: 360, locked: 360)],
            Week,
            At);

        Assert.True(selected.IsSelected);
        Assert.Equal(At.AddDays(-30), selected.SelectedAt);
        Assert.False(unselected.IsSelected);
        Assert.Null(selected.Score);
    }

    private static CandidateChannel Candidate(int service, int channel)
        => CandidateChannel.Discover(
            CandidateChannelId.New(),
            Network,
            new ServiceId(service),
            TuningParameters.Terrestrial(channel),
            At.AddDays(-60));

    private static CandidateChannel Selected(int service, int channel, DateTime since)
    {
        CandidateChannel candidate = Candidate(service, channel);
        candidate.Select(SelectionSource.Manual, null, since);

        return candidate;
    }

    private static IntendedStream Stream(int channel, params int[] services)
        => new(
            Network,
            null,
            TuningParameters.Terrestrial(channel),
            [.. services.Select(service => new ServiceId(service))],
            StreamReach.Reachable);

    private static QualitySignalWindow Window(
        DateTime start,
        int service,
        long samples,
        long locked,
        long unreachable = 0,
        int? carrierToNoise = null,
        double? errorRate = null)
        => new(
            start,
            new TunerDeviceId("adapter0.frontend0"),
            Network,
            new ServiceId(service),
            samples,
            locked,
            0,
            unreachable,
            carrierToNoise,
            errorRate is { } rate ? [new LayerErrorPeak(0, rate)] : [],
            [],
            carrierToNoise is null && errorRate is null ? null : start);
}
