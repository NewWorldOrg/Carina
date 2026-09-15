using Carina.Contracts;

namespace Carina.Driver.Sessions;

public static class SessionFailureTitles
{
    private const int NoSpaceLeftOnDevice = 28;

    public static string? Of(Exception? cause)
        => TuningFailureTitles.Of(cause)
            ?? (RanOutOfRoom(cause) ? SessionRefusalTitles.DiskFull : null);

    private static bool RanOutOfRoom(Exception? cause)
        => cause switch
        {
            null => false,
            IOException { HResult: NoSpaceLeftOnDevice } => true,
            AggregateException many => many.InnerExceptions.Any(RanOutOfRoom),
            _ => RanOutOfRoom(cause.InnerException),
        };
}
