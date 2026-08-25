namespace Carina.Driver.Sessions;

public sealed class RecordingProgressNotifier : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);

    private readonly Func<bool> anythingRecording;
    private readonly Action tell;
    private readonly ITimer timer;

    private long notices;

    public RecordingProgressNotifier(
        Func<bool> anythingRecording,
        Action tell,
        TimeProvider timeProvider,
        TimeSpan? every = null
    )
    {
        this.anythingRecording = anythingRecording;
        this.tell = tell;

        TimeSpan interval = every ?? DefaultInterval;

        timer = timeProvider.CreateTimer(_ => Tick(), null, interval, interval);
    }

    public long Notices => Interlocked.Read(ref notices);

    public void Dispose() => timer.Dispose();

    private void Tick()
    {
        if (!anythingRecording())
        {
            return;
        }

        Interlocked.Increment(ref notices);
        tell();
    }
}
