using Carina.Infrastructure.Recordings;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingReadTurnTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-BS-001: one pass reads a recording through at a time, and the next takes its turn once the first gives it back")]
    public async Task OnePassReadsARecordingThroughAtATime()
    {
        using RecordingReadTurn turn = new();
        IDisposable first = await turn.TakeAsync(Cancel);

        Task<IDisposable> second = turn.TakeAsync(Cancel);

        Assert.True(turn.Held);
        Assert.False(second.IsCompleted);

        first.Dispose();
        first.Dispose();
        (await second).Dispose();

        Assert.False(turn.Held);
    }

    [Fact]
    public async Task AWaitCalledOffLeavesTheTurnWithWhoeverHasIt()
    {
        using RecordingReadTurn turn = new();
        using CancellationTokenSource calling = new();
        IDisposable first = await turn.TakeAsync(Cancel);

        Task<IDisposable> second = turn.TakeAsync(calling.Token);
        await calling.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.True(turn.Held);
        first.Dispose();
        Assert.False(turn.Held);
    }
}
