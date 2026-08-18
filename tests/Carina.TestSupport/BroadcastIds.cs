namespace Carina.TestSupport;

public static class BroadcastIds
{
    private const int First = 10_000;

    private const int Beyond = 60_000;

    private static int handedOut;

    public static int NextNetwork()
        => First + (Interlocked.Increment(ref handedOut) % (Beyond - First));
}
