namespace Carina.Driver.Sessions;

public sealed class RecordingProgressNotifier : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);

    private readonly Func<bool> anythingRecording;
    private readonly Action tell;
    private readonly Action<Exception> faulted;
    private readonly ITimer timer;

    private long notices;
    private long faults;

    public RecordingProgressNotifier(
        Func<bool> anythingRecording,
        Action tell,
        TimeProvider timeProvider,
        Action<Exception> faulted,
        TimeSpan? every = null
    )
    {
        this.anythingRecording = anythingRecording;
        this.tell = tell;
        this.faulted = faulted;

        TimeSpan interval = every ?? DefaultInterval;

        timer = timeProvider.CreateTimer(_ => Tick(), null, interval, interval);
    }

    public long Notices => Interlocked.Read(ref notices);

    public long Faults => Interlocked.Read(ref faults);

    public void Dispose() => timer.Dispose();

    private void Tick()
    {
        try
        {
            if (!anythingRecording())
            {
                return;
            }

            tell();
            Interlocked.Increment(ref notices);
        }
        catch (Exception error)
        {
            Interlocked.Increment(ref faults);
            faulted.Invoke(error);
        }
    }
}
