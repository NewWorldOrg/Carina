namespace Carina.Driver.Tuning.Usb;

/// <summary>
/// The clock and the sleep a device is driven by, so that a test can stand in for both.
/// </summary>
public sealed class DevicePacing(TimeProvider time, Action<TimeSpan> sleep)
{
    public static DevicePacing RealTime(TimeProvider time) => new(time, Thread.Sleep);

    public DateTimeOffset Now => time.GetUtcNow();

    public void Sleep(TimeSpan span) => sleep(span);

    public void SleepMilliseconds(int milliseconds) => sleep(TimeSpan.FromMilliseconds(milliseconds));
}
