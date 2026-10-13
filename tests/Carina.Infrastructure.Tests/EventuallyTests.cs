using Carina.TestSupport;

namespace Carina.Infrastructure.Tests;

public sealed class EventuallyTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

    [Fact(DisplayName = "a condition that comes true as the patience runs out is taken as having happened, however long each look takes")]
    public async Task AConditionThatComesTrueAsThePatienceRunsOutIsTakenAsHavingHappened()
    {
        long start = Environment.TickCount64;

        await Eventually.Happens(
            () =>
            {
                bool passed = Environment.TickCount64 - start >= Short.TotalMilliseconds;
                Thread.Sleep(Short);

                return passed;
            },
            "the span passes",
            Short);
    }

    [Fact(DisplayName = "an answer that arrives as the patience runs out is handed back rather than refused, however long it takes to come")]
    public async Task AnAnswerThatArrivesAsThePatienceRunsOutIsHandedBack()
    {
        long start = Environment.TickCount64;

        long waited = await Eventually.Yields(
            async () =>
            {
                long elapsed = Environment.TickCount64 - start;
                await Task.Delay(Short);

                return elapsed;
            },
            elapsed => elapsed >= Short.TotalMilliseconds,
            elapsed => $"{elapsed} ms",
            "the span passes",
            Short);

        Assert.True(waited >= Short.TotalMilliseconds);
    }

    [Fact(DisplayName = "a condition that never comes true is reported once the patience runs out")]
    public async Task AConditionThatNeverComesTrueIsReported()
    {
        TimeoutException refused = await Assert.ThrowsAsync<TimeoutException>(
            () => Eventually.Happens(() => false, "nothing", Short));

        Assert.Contains("nothing", refused.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "an answer that never comes is reported with the last one seen")]
    public async Task AnAnswerThatNeverComesIsReportedWithTheLastOneSeen()
    {
        TimeoutException refused = await Assert.ThrowsAsync<TimeoutException>(
            () => Eventually.Yields(() => Task.FromResult(7), _ => false, seen => $"seen {seen}", "nothing", Short));

        Assert.Contains("Last seen: seen 7", refused.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a condition already true is not waited on")]
    public async Task AConditionAlreadyTrueIsNotWaitedOn()
    {
        int asked = 0;

        await Eventually.Happens(
            () =>
            {
                asked++;

                return true;
            },
            "at once",
            TimeSpan.Zero);

        Assert.Equal(1, asked);
    }
}
