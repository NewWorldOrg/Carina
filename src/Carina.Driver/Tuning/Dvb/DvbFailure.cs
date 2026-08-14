using System.Runtime.InteropServices;

namespace Carina.Driver.Tuning.Dvb;

public sealed class DvbDeviceException(string message) : Exception(message);

public static class Errno
{
    public const int Interrupted = 4;
    public const int WouldBlock = 11;
    public const int Busy = 16;
    public const int NoSuchDevice = 19;
    public const int Overflowed = 75;
}

public static class DvbFailure
{
    public static DvbDeviceException Refused(string what) => new(what);

    public static DvbDeviceException AtDevice(
        string devicePath,
        string operation,
        int error,
        string consequence
    ) => new($"{devicePath}: {operation} failed — {Describe(error)}. {consequence}");

    public static string Describe(int error) =>
        $"errno {error} ({Marshal.GetPInvokeErrorMessage(error)})";
}
