using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tuning;

public interface ISignalQualitySource
{
    SignalQuality Measure();
}

public sealed record SignalQualitySample(
    DateTimeOffset MeasuredAt,
    DateTimeOffset LockReadAt,
    SignalQuality? Quality
)
{
    public bool Readable => Quality is not null;

    public bool HasLock => Quality?.HasLock is true;

    public bool LostLock => Quality?.Locked.HeldAtNeitherEnd is true;
}

public sealed class SignalQualityReader
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan WhileWalkingChannels = TimeSpan.FromSeconds(2);

    private readonly ISignalQualitySource source;
    private readonly TimeProvider time;
    private readonly TimeSpan interval;
    private readonly Action<SignalQualitySample>? lockLost;
    private readonly Action<Exception>? problem;
    private readonly Lock gate = new();

    private SignalQualitySample? latest;
    private DateTimeOffset? readAt;
    private bool holdingLock = true;
    private long lockLosses;

    public SignalQualityReader(
        ISignalQualitySource source,
        TimeProvider time,
        TimeSpan interval,
        Action<SignalQualitySample>? lockLost = null,
        Action<Exception>? problem = null
    )
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                $"Readings are taken on an interval above zero; got {interval}."
            );
        }

        this.source = source;
        this.time = time;
        this.interval = interval;
        this.lockLost = lockLost;
        this.problem = problem;
    }

    public long LockLosses => Interlocked.Read(ref lockLosses);

    public SignalQualitySample? Latest
    {
        get
        {
            lock (gate)
            {
                return latest;
            }
        }
    }

    public bool ReadIfDue()
    {
        DateTimeOffset now = time.GetUtcNow();

        lock (gate)
        {
            if (readAt is { } last && now - last < interval)
            {
                return false;
            }
        }

        Read();

        return true;
    }

    public SignalQualitySample Read()
    {
        DateTimeOffset startedAt = time.GetUtcNow();
        SignalQuality? quality = null;
        Exception? refusal = null;

        try
        {
            quality = source.Measure();
        }
        catch (DvbDeviceException error)
        {
            refusal = error;
        }

        var sample = new SignalQualitySample(startedAt, time.GetUtcNow(), quality);

        lock (gate)
        {
            latest = sample;
            readAt = sample.MeasuredAt;
        }

        if (refusal is not null)
        {
            problem?.Invoke(refusal);

            return sample;
        }

        Judge(sample);

        return sample;
    }

    private void Judge(SignalQualitySample sample)
    {
        if (sample.HasLock)
        {
            holdingLock = true;

            return;
        }

        if (!sample.LostLock || !holdingLock)
        {
            return;
        }

        holdingLock = false;
        Interlocked.Increment(ref lockLosses);
        lockLost?.Invoke(sample);
    }
}
