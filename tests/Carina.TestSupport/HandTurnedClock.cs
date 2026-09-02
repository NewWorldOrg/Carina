namespace Carina.TestSupport;

public sealed class HandTurnedClock(DateTimeOffset from) : TimeProvider
{
    private readonly Lock gate = new();

    private readonly List<Alarm> alarms = [];

    private DateTimeOffset now = from;

    public HandTurnedClock()
        : this(new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero))
    {
    }

    public int Pending
    {
        get
        {
            lock (gate)
            {
                return alarms.Count;
            }
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
        {
            return now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (period != Timeout.InfiniteTimeSpan)
        {
            throw new NotSupportedException("This clock rings an alarm once; nothing here asks for a repeating one.");
        }

        Alarm alarm = new(this, callback, state);

        alarm.Change(dueTime, period);

        return alarm;
    }

    public void Turn(TimeSpan by)
    {
        List<Alarm> ringing;

        lock (gate)
        {
            now = now.Add(by);
            ringing = [.. alarms.Where(alarm => alarm.Due <= now)];

            foreach (Alarm alarm in ringing)
            {
                alarms.Remove(alarm);
            }
        }

        foreach (Alarm alarm in ringing)
        {
            alarm.Ring();
        }
    }

    private sealed class Alarm(HandTurnedClock clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock.gate)
            {
                clock.alarms.Remove(this);

                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    return true;
                }

                Due = clock.now.Add(dueTime);
                clock.alarms.Add(this);

                return true;
            }
        }

        public void Ring() => callback(state);

        public void Dispose()
        {
            lock (clock.gate)
            {
                clock.alarms.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
