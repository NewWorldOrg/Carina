using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class StartRetryTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 20, 10, 0, DateTimeKind.Utc);

    private static readonly RetryPolicy Policy = new(3, TimeSpan.FromMinutes(1));

    [Fact]
    public void EveryClassThereIsIsWeighedAndOnlyTheTwoPassingOnesAreTriedAgain()
    {
        foreach (TuneFailureKind failure in Enum.GetValues<TuneFailureKind>())
        {
            RetryMove expected = failure is TuneFailureKind.NoLock or TuneFailureKind.NoData
                ? RetryMove.Retry
                : RetryMove.GiveUp;

            Assert.Equal(expected, StartRetry.For(Sighting(classes: [failure]), Policy, Now).Move);
        }

        Assert.Equal([TuneFailureKind.NoLock, TuneFailureKind.NoData], StartRetry.Transient);
    }

    [Theory]
    [InlineData(TuneFailureKind.NoLock, true)]
    [InlineData(TuneFailureKind.NoData, true)]
    [InlineData(TuneFailureKind.IncompletePsi, false)]
    [InlineData(TuneFailureKind.StreamMismatch, false)]
    public void OnlyAStartThatDidNotLockOrGotNoDataIsTriedAgain(TuneFailureKind failure, bool transient)
    {
        RetryVerdict verdict = StartRetry.For(Sighting(classes: [failure]), Policy, Now);

        if (transient)
        {
            Assert.Equal(RetryMove.Retry, verdict.Move);
            Assert.Null(verdict.Reason);
        }
        else
        {
            Assert.Equal(RetryMove.GiveUp, verdict.Move);
            Assert.Equal(RetryGiveUp.NotTransient, verdict.Reason);
        }
    }

    [Theory]
    [InlineData(TuneFailureKind.IncompletePsi)]
    [InlineData(TuneFailureKind.StreamMismatch)]
    public void OneStructuralFailureAmongTransientOnesIsEnoughToGiveUp(TuneFailureKind structural)
    {
        RetryVerdict verdict = StartRetry.For(
            Sighting(classes: [TuneFailureKind.NoLock, structural, TuneFailureKind.NoData]),
            Policy,
            Now);

        Assert.Equal(RetryGiveUp.NotTransient, verdict.Reason);
        Assert.Equal(structural, StartRetry.StructuralAmong([TuneFailureKind.NoLock, structural]));
        Assert.Null(StartRetry.StructuralAmong([TuneFailureKind.NoLock, TuneFailureKind.NoData]));
    }

    [Theory]
    [InlineData(true, false, true, 0, RetryGiveUp.PrecheckFailed)]
    [InlineData(false, true, true, 0, RetryGiveUp.CandidateNeedsAttention)]
    [InlineData(false, false, false, 0, RetryGiveUp.BroadcastOver)]
    [InlineData(false, false, true, 3, RetryGiveUp.AttemptsSpent)]
    [InlineData(false, false, true, 4, RetryGiveUp.AttemptsSpent)]
    [InlineData(true, true, false, 3, RetryGiveUp.PrecheckFailed)]
    [InlineData(false, true, false, 3, RetryGiveUp.CandidateNeedsAttention)]
    [InlineData(false, false, false, 3, RetryGiveUp.BroadcastOver)]
    public void EachReasonNotToTryAgainIsNamedAndTheOrderTheyAreAskedInIsFixed(
        bool precheckFailed,
        bool needsAttention,
        bool stillOnAir,
        int attempts,
        RetryGiveUp expected)
    {
        RetryVerdict verdict = StartRetry.For(
            Sighting(
                precheckFailed: precheckFailed,
                needsAttention: needsAttention,
                stillOnAir: stillOnAir,
                attempts: attempts),
            Policy,
            Now);

        Assert.Equal(RetryMove.GiveUp, verdict.Move);
        Assert.Equal(expected, verdict.Reason);
        Assert.Null(verdict.NotBefore);
    }

    [Fact]
    public void AStructuralFailureIsNamedBeforeAnyOtherReason()
        => Assert.Equal(
            RetryGiveUp.NotTransient,
            StartRetry.For(
                Sighting(
                    classes: [TuneFailureKind.StreamMismatch],
                    precheckFailed: true,
                    needsAttention: true,
                    stillOnAir: false,
                    attempts: 9),
                Policy,
                Now).Reason);

    [Fact]
    public void EveryReasonToGiveUpIsOneTheDecisionCanReach()
    {
        HashSet<RetryGiveUp> reached =
        [
            StartRetry.For(Sighting(classes: [TuneFailureKind.StreamMismatch]), Policy, Now).Reason!.Value,
            StartRetry.For(Sighting(precheckFailed: true), Policy, Now).Reason!.Value,
            StartRetry.For(Sighting(needsAttention: true), Policy, Now).Reason!.Value,
            StartRetry.For(Sighting(stillOnAir: false), Policy, Now).Reason!.Value,
            StartRetry.For(Sighting(attempts: 3), Policy, Now).Reason!.Value,
        ];

        Assert.Equal(Enum.GetValues<RetryGiveUp>().Order(), reached.Order());
    }

    [Fact]
    public void AStartIsNotTriedAgainUntilTheIntervalHasPassedSinceTheLastOne()
    {
        DateTime last = Now.AddSeconds(-30);
        RetryVerdict waiting = StartRetry.For(Sighting(lastAttemptAt: last), Policy, Now);

        Assert.Equal(RetryMove.Wait, waiting.Move);
        Assert.Equal(last.AddMinutes(1), waiting.NotBefore);
        Assert.Null(waiting.Reason);

        Assert.Equal(RetryMove.Wait, StartRetry.For(Sighting(lastAttemptAt: last), Policy, last.AddMinutes(1).AddTicks(-1)).Move);
        Assert.Equal(RetryMove.Retry, StartRetry.For(Sighting(lastAttemptAt: last), Policy, last.AddMinutes(1)).Move);
    }

    [Fact]
    public void AChannelThatIsBackingOffIsWaitedForPastTheInterval()
    {
        DateTime last = Now.AddMinutes(-5);
        DateTime rests = Now.AddMinutes(3);

        RetryVerdict verdict = StartRetry.For(Sighting(lastAttemptAt: last, restsUntil: rests), Policy, Now);

        Assert.Equal(RetryMove.Wait, verdict.Move);
        Assert.Equal(rests, verdict.NotBefore);
        Assert.Equal(RetryMove.Retry, StartRetry.For(Sighting(lastAttemptAt: last, restsUntil: Now), Policy, Now).Move);
    }

    [Fact]
    public void ARestThatEndsBeforeTheIntervalDoesNotShortenIt()
    {
        DateTime last = Now.AddSeconds(-10);

        RetryVerdict verdict = StartRetry.For(
            Sighting(lastAttemptAt: last, restsUntil: Now.AddSeconds(5)),
            Policy,
            Now);

        Assert.Equal(last.AddMinutes(1), verdict.NotBefore);
    }

    [Fact]
    public void APolicyThatAllowsNoAttemptsGivesUpAtOnce()
        => Assert.Equal(
            RetryGiveUp.AttemptsSpent,
            StartRetry.For(Sighting(attempts: 0), new RetryPolicy(0, TimeSpan.FromMinutes(1)), Now).Reason);

    [Fact]
    public void TheLastAttemptBelowTheCeilingIsStillMade()
        => Assert.Equal(RetryMove.Retry, StartRetry.For(Sighting(attempts: 2), Policy, Now).Move);

    [Fact]
    public void ASightingWithNoClassToWeighIsRefused()
        => Assert.Throws<ArgumentException>(() => StartRetry.For(Sighting(classes: []), Policy, Now));

    [Fact]
    public void AClassOutsideTheFourIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => StartRetry.For(Sighting(classes: [(TuneFailureKind)99]), Policy, Now));

    [Fact]
    public void ANegativeCountOfAttemptsIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => StartRetry.For(Sighting(attempts: -1), Policy, Now));

    [Fact]
    public void ALocalTimeIsRefused()
        => Assert.Throws<ArgumentException>(
            () => StartRetry.For(Sighting(), Policy, DateTime.SpecifyKind(Now, DateTimeKind.Local)));

    [Fact]
    public void ThePolicyComesConservative()
    {
        Assert.Equal(3, RetryPolicy.Default.MostAttempts);
        Assert.Equal(TimeSpan.FromMinutes(1), RetryPolicy.Default.BetweenAttempts);
    }

    [Theory]
    [InlineData(-1, 60)]
    [InlineData(21, 60)]
    [InlineData(3, 0)]
    [InlineData(3, -1)]
    [InlineData(3, 86401)]
    public void APolicyThatCannotBeHeldIsRefused(int attempts, int seconds)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new RetryPolicy(attempts, TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(20, 86400)]
    public void ThePolicyTakesItsBounds(int attempts, int seconds)
    {
        var policy = new RetryPolicy(attempts, TimeSpan.FromSeconds(seconds));

        Assert.Equal(attempts, policy.MostAttempts);
        Assert.Equal(TimeSpan.FromSeconds(seconds), policy.BetweenAttempts);
    }

    private static RetrySighting Sighting(
        IReadOnlyList<TuneFailureKind>? classes = null,
        bool stillOnAir = true,
        int attempts = 0,
        DateTime? lastAttemptAt = null,
        bool needsAttention = false,
        DateTime? restsUntil = null,
        bool precheckFailed = false)
        => new(
            classes ?? [TuneFailureKind.NoLock],
            stillOnAir,
            attempts,
            lastAttemptAt ?? Now.AddHours(-1),
            needsAttention,
            restsUntil,
            precheckFailed);
}
