namespace Carina.Infrastructure.Recordings;

public sealed class RecordingReadTurn : IDisposable
{
    private readonly SemaphoreSlim one = new(1, 1);

    public bool Held => one.CurrentCount is 0;

    public async Task<IDisposable> TakeAsync(CancellationToken cancellationToken)
    {
        await one.WaitAsync(cancellationToken);

        return new Turn(one);
    }

    public void Dispose() => one.Dispose();

    private sealed class Turn(SemaphoreSlim one) : IDisposable
    {
        private int given;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref given, 1) is 0)
            {
                one.Release();
            }
        }
    }
}
