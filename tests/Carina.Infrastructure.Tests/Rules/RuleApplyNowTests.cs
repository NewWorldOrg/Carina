using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Rules;

namespace Carina.Infrastructure.Tests.Rules;

public sealed class RuleApplyNowTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan LongEnoughToTellAStallFromAWait = TimeSpan.FromSeconds(10);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task AskingForTheRulesToBeAppliedRunsAPassAndAnswersWithWhatItDid()
    {
        var passes = new CountedPasses();
        var notices = new WrittenNotices();
        RuleApplyNow applying = Applying(passes, notices);

        RuleApplyOutcome outcome = await Within(applying.StartAsync(Cancel));

        Assert.Null(outcome.Refusal);
        Assert.NotNull(outcome.Run);
        Assert.Equal(1, passes.Ran);
        Assert.Equal([RecalculationTrigger.RulesChanged], notices.Rung);
    }

    [Fact]
    public async Task WhatIsAskedForReachesEverythingRatherThanOnlyTheTuners()
    {
        Assert.Equal(
            RecalculationReach.Everything,
            RecalculationReaches.Of(RecalculationTrigger.RulesChanged));
    }

    [Fact]
    public async Task ASecondApplicationWhileTheFirstIsWalkingIsRefusedAndNamesTheOneWalking()
    {
        var passes = new CountedPasses { Held = new TaskCompletionSource() };
        RuleApplyNow applying = Applying(passes);

        Task<RuleApplyOutcome> first = applying.StartAsync(Cancel);
        await Within(passes.Entered.Task);

        RuleApplyOutcome second = await Within(applying.StartAsync(Cancel));

        passes.Held.SetResult();

        RuleApplyOutcome finished = await Within(first);

        Assert.Null(second.Run);
        Assert.NotNull(second.Refusal);
        Assert.Equal(RuleApplyRefusal.OneIsAlreadyRunning, second.Refusal.Refusal);
        Assert.NotNull(finished.Run);
        Assert.Equal(finished.Run.ApplyId, second.Refusal.RunningId);
        Assert.Equal(1, passes.Ran);
    }

    [Fact]
    public async Task NoTwoApplicationsAreEverInsideThePassAtTheSameTime()
    {
        var passes = new CountedPasses();
        RuleApplyNow applying = Applying(passes);

        Task<RuleApplyOutcome>[] asked =
        [
            .. Enumerable.Range(0, 8).Select(_ => Task.Run(() => applying.StartAsync(Cancel), Cancel)),
        ];

        await Within(Task.WhenAll(asked));

        Assert.Equal(1, passes.Deepest);
        Assert.True(passes.Ran >= 1);
    }

    [Fact]
    public async Task AnApplicationTooSoonAfterTheLastOneIsRefusedWithTheMomentItMayBeAskedForAgain()
    {
        var clock = new MovingClock(Now);
        var passes = new CountedPasses();
        RuleApplyNow applying = Applying(passes, clock: clock);

        await Within(applying.StartAsync(Cancel));
        clock.Move(TimeSpan.FromSeconds(30));

        RuleApplyOutcome refused = await Within(applying.StartAsync(Cancel));

        Assert.NotNull(refused.Refusal);
        Assert.Equal(RuleApplyRefusal.TooSoonAfterTheLastOne, refused.Refusal.Refusal);
        Assert.Equal(Now + RuleApplySettings.DefaultBetweenApplications, refused.Refusal.NotBefore);
        Assert.Equal(1, passes.Ran);
    }

    [Fact]
    public async Task AnApplicationAskedForOnceTheCooldownIsUpRunsAgain()
    {
        var clock = new MovingClock(Now);
        var passes = new CountedPasses();
        RuleApplyNow applying = Applying(passes, clock: clock);

        await Within(applying.StartAsync(Cancel));
        clock.Move(RuleApplySettings.DefaultBetweenApplications);

        RuleApplyOutcome again = await Within(applying.StartAsync(Cancel));

        Assert.Null(again.Refusal);
        Assert.Equal(2, passes.Ran);
    }

    [Fact]
    public async Task AStandingRecalculationHoldingTheFloorIsAnsweredAsSuchRatherThanWaitedOn()
    {
        var passes = new CountedPasses
        {
            Answers = RecalculationPass.Refused(RecalculationRefusal.OneIsAlreadyRunning),
        };
        RuleApplyNow applying = Applying(passes);

        RuleApplyOutcome outcome = await Within(applying.StartAsync(Cancel));

        Assert.Null(outcome.Run);
        Assert.NotNull(outcome.Refusal);
        Assert.Equal(RuleApplyRefusal.ARecalculationIsAlreadyRunning, outcome.Refusal.Refusal);
    }

    [Fact]
    public async Task APassThatFailsLeavesTheFloorFreeForTheNextAsking()
    {
        var clock = new MovingClock(Now);
        var passes = new CountedPasses { Throws = new InvalidOperationException("the pass failed") };
        RuleApplyNow applying = Applying(passes, clock: clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Within(applying.StartAsync(Cancel)));

        clock.Move(RuleApplySettings.DefaultBetweenApplications);
        passes.Throws = null;

        Assert.Null((await Within(applying.StartAsync(Cancel))).Refusal);
    }

    private static RuleApplyNow Applying(
        CountedPasses passes,
        WrittenNotices? notices = null,
        MovingClock? clock = null)
        => new(
            notices ?? new WrittenNotices(),
            passes,
            new RuleApplySettings(),
            clock ?? new MovingClock(Now));

    private static Task<T> Within<T>(Task<T> asked) => asked.WaitAsync(LongEnoughToTellAStallFromAWait);

    private static Task Within(Task asked) => asked.WaitAsync(LongEnoughToTellAStallFromAWait);

    private sealed class MovingClock(DateTime from) : TimeProvider
    {
        private DateTime now = from;

        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);

        public void Move(TimeSpan by) => now += by;
    }

    private sealed class WrittenNotices : IRecalculationNotice
    {
        private readonly List<RecalculationTrigger> rung = [];

        public IReadOnlyList<RecalculationTrigger> Rung
        {
            get
            {
                lock (rung)
                {
                    return [.. rung];
                }
            }
        }

        public void Nudge(RecalculationTrigger trigger)
        {
            lock (rung)
            {
                rung.Add(trigger);
            }
        }
    }

    private sealed class CountedPasses : IRecalculationPass
    {
        private int inside;

        public int Ran { get; private set; }

        public int Deepest { get; private set; }

        public TaskCompletionSource? Held { get; init; }

        public TaskCompletionSource Entered { get; } = new();

        public RecalculationPass? Answers { get; init; }

        public Exception? Throws { get; set; }

        public async Task<RecalculationPass> RunAsync(CancellationToken cancellationToken)
        {
            int depth = Interlocked.Increment(ref inside);

            lock (this)
            {
                Ran++;
                Deepest = Math.Max(Deepest, depth);
            }

            Entered.TrySetResult();

            try
            {
                if (Held is { } gate)
                {
                    await gate.Task.WaitAsync(LongEnoughToTellAStallFromAWait, cancellationToken);
                }

                return Throws is { } failure
                    ? throw failure
                    : Answers ?? RecalculationPass.Of(
                        [RecalculationTrigger.RulesChanged],
                        RecalculationReach.Everything,
                        7,
                        null,
                        null,
                        []);
            }
            finally
            {
                Interlocked.Decrement(ref inside);
            }
        }
    }
}
