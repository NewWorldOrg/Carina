namespace Carina.Infrastructure.Driver;

public sealed class SignalledWait(TimeProvider clock)
{
    private CancellationTokenSource? waking;

    private int signalled;

    public void Signal()
    {
        Interlocked.Exchange(ref signalled, 1);

        try
        {
            Volatile.Read(ref waking)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }
    }

    public async Task<bool> WaitAsync(TimeSpan waiting, CancellationToken stoppingToken)
    {
        using CancellationTokenSource woken = new();

        Volatile.Write(ref waking, woken);

        try
        {
            if (Interlocked.Exchange(ref signalled, 0) == 1)
            {
                return !stoppingToken.IsCancellationRequested;
            }

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(woken.Token, stoppingToken);

            await Task.Delay(waiting, clock, linked.Token);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Exchange(ref signalled, 0);

            return !stoppingToken.IsCancellationRequested;
        }
        finally
        {
            Volatile.Write(ref waking, null);
        }

        return true;
    }
}
