namespace Carina.Driver.Sessions;

public sealed class SettledOnce
{
    private readonly TaskCompletionSource finished = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    private int settled;

    public Task Finished => finished.Task;

    public bool IsSettled => Volatile.Read(ref settled) is 1;

    public bool TrySettle() => Interlocked.Exchange(ref settled, 1) is 0;

    public void HasFinished() => finished.TrySetResult();

    public bool SettleUnlessAnotherAlreadyHas()
    {
        if (TrySettle())
        {
            return true;
        }

        finished.Task.GetAwaiter().GetResult();

        return false;
    }
}
