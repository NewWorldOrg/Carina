namespace Carina.Driver;

public sealed class DriverStopRequest
{
    private int asked;

    public bool WasAsked => Volatile.Read(ref asked) is not 0;

    public void Record() => Volatile.Write(ref asked, 1);
}
