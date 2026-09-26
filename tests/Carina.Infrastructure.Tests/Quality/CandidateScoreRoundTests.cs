using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Quality;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Quality;

public sealed class CandidateScoreRoundTests
{
    private static readonly DateTime At = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly NetworkId Network = new(4);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "what was measured on a candidate's channel is written back to the candidate")]
    public async Task WhatWasMeasuredOnACandidatesChannelIsWrittenBackToTheCandidate()
    {
        HeldCandidates candidates = new();
        HeldQualitySignals signals = new();
        CandidateChannel candidate = Selected(candidates, 101, 27, At.AddDays(-30));
        signals.Windows.Add(Window(At.AddHours(-2), 101, samples: 360, locked: 350));

        int written = await Round(candidates, signals).RunAsync(Cancel);

        Assert.Equal(1, written);
        Assert.Equal(360, candidate.Score?.Samples);
        Assert.Equal(350.0 / 360, candidate.Score?.LockRate);
        Assert.Equal(At.AddDays(-7), candidate.Score?.MeasuredFrom);
        Assert.Equal(At, candidate.Score?.EvaluatedAt);
    }

    [Fact(DisplayName = "writing scores back leaves every selection as it was")]
    public async Task WritingScoresBackLeavesEverySelectionAsItWas()
    {
        HeldCandidates candidates = new();
        HeldQualitySignals signals = new();
        CandidateChannel onTheMeasuredChannel = Selected(candidates, 101, 27, At.AddDays(-30));
        CandidateChannel sameChannelUnselected = Candidate(candidates, 102, 27);
        CandidateChannel selectedElsewhere = Selected(candidates, 102, 30, At.AddDays(-20));
        signals.Windows.Add(Window(At.AddHours(-2), 101, samples: 360, locked: 360));

        await Round(candidates, signals).RunAsync(Cancel);

        Assert.NotNull(sameChannelUnselected.Score);
        Assert.False(sameChannelUnselected.IsSelected);
        Assert.True(onTheMeasuredChannel.IsSelected);
        Assert.Equal(At.AddDays(-30), onTheMeasuredChannel.SelectedAt);
        Assert.True(selectedElsewhere.IsSelected);
        Assert.Equal(At.AddDays(-20), selectedElsewhere.SelectedAt);
    }

    [Fact(DisplayName = "a candidate nothing was sampled on keeps the score it had")]
    public async Task ACandidateNothingWasSampledOnKeepsTheScoreItHad()
    {
        HeldCandidates candidates = new();
        HeldQualitySignals signals = new();
        CandidateChannel candidate = Selected(candidates, 101, 27, At.AddDays(-30));
        CandidateScore earlier = CandidateScore.Of(360, 360, 24_000, 0, At.AddDays(-9), At.AddDays(-2), At.AddDays(-2));
        candidate.Evaluated(earlier);

        int written = await Round(candidates, signals).RunAsync(Cancel);

        Assert.Equal(0, written);
        Assert.Same(earlier, candidate.Score);
    }

    [Fact]
    public async Task TheScoresAreReadOverThePeriodTheSettingsName()
    {
        HeldCandidates candidates = new();
        HeldQualitySignals signals = new();

        await Round(candidates, signals, TimeSpan.FromDays(3)).RunAsync(Cancel);

        QualityPeriod asked = Assert.Single(signals.Asked);
        Assert.Equal(At.AddDays(-3), asked.From);
        Assert.Equal(At, asked.Until);
    }

    private static CandidateScoreRound Round(
        HeldCandidates candidates,
        HeldQualitySignals signals,
        TimeSpan? over = null)
        => new(
            candidates,
            new BroadcastStreamDirectory(candidates),
            signals,
            new QualitySignalSettings { EvaluateCandidatesOver = over ?? TimeSpan.FromDays(7) },
            new StoppedClock(At));

    private static CandidateChannel Candidate(HeldCandidates candidates, int service, int channel)
    {
        CandidateChannel candidate = CandidateChannel.Discover(
            CandidateChannelId.New(),
            Network,
            new ServiceId(service),
            TuningParameters.Terrestrial(channel),
            At.AddDays(-60));

        candidates.Candidates.Add(candidate);

        return candidate;
    }

    private static CandidateChannel Selected(HeldCandidates candidates, int service, int channel, DateTime since)
    {
        CandidateChannel candidate = Candidate(candidates, service, channel);
        candidate.Select(SelectionSource.Manual, null, since);

        return candidate;
    }

    private static QualitySignalWindow Window(DateTime start, int service, long samples, long locked)
        => new(
            start,
            new TunerDeviceId("adapter0.frontend0"),
            Network,
            new ServiceId(service),
            samples,
            locked,
            0,
            0,
            null,
            [],
            [],
            null);

    private sealed class StoppedClock(DateTime at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(at);
    }
}
