using Carina.Infrastructure.Driver;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Driver;

public sealed class SignalledWaitTests
{
    private static readonly TimeSpan AnHour = TimeSpan.FromHours(1);

    [Fact(DisplayName = "a signal that came while nobody was waiting ends the next wait at once, so the run it asked for is not lost")]
    public async Task ASignalWhileNobodyWaitedEndsTheNextWaitAtOnce()
    {
        SignalledWait wait = new(TimeProvider.System);

        wait.Signal();
        Task<bool> waiting = wait.WaitAsync(AnHour, CancellationToken.None);

        Assert.True(waiting.IsCompleted);
        Assert.True(await waiting);
    }

    [Fact(DisplayName = "a signal while the wait is under way ends it")]
    public async Task ASignalDuringTheWaitEndsIt()
    {
        SignalledWait wait = new(TimeProvider.System);
        Task<bool> waiting = wait.WaitAsync(AnHour, CancellationToken.None);

        Assert.False(waiting.IsCompleted);
        wait.Signal();

        Assert.True(await waiting.WaitAsync(Eventually.Patience));
    }

    [Fact(DisplayName = "one signal ends one wait, and the wait after it lasts its time")]
    public async Task OneSignalEndsOneWait()
    {
        SignalledWait wait = new(TimeProvider.System);
        using CancellationTokenSource stopping = new();

        wait.Signal();
        bool first = await wait.WaitAsync(AnHour, stopping.Token);
        Task<bool> second = wait.WaitAsync(AnHour, stopping.Token);

        Assert.True(first);
        Assert.False(second.IsCompleted);
        await stopping.CancelAsync();
        Assert.False(await second.WaitAsync(Eventually.Patience));
    }

    [Fact(DisplayName = "stopping ends the wait as stopped, even with a signal kept")]
    public async Task StoppingEndsTheWaitAsStopped()
    {
        SignalledWait wait = new(TimeProvider.System);
        using CancellationTokenSource stopping = new();
        await stopping.CancelAsync();

        wait.Signal();

        Assert.False(await wait.WaitAsync(AnHour, stopping.Token));
    }

    [Fact(DisplayName = "a wait that no signal ends lasts its time and then lets the next run begin")]
    public async Task AWaitNoSignalEndsLastsItsTime()
    {
        SignalledWait wait = new(TimeProvider.System);

        Assert.True(await wait.WaitAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None).WaitAsync(Eventually.Patience));
    }
}
