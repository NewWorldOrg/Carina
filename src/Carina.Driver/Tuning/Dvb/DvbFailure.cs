using System.Runtime.InteropServices;

namespace Carina.Driver.Tuning.Dvb;

public enum TuningFailure
{
    Unspecified = 0,

    DeviceUnusable = 1,

    NoLock = 2,

    LockedWithoutData = 3,
}

public sealed class DvbDeviceException(string message, TuningFailure failure, int error = 0)
    : Exception(message)
{
    public TuningFailure Failure { get; } = failure;

    public int Error { get; } = error;
}

public static class Errno
{
    public const int NotPermitted = 1;
    public const int Interrupted = 4;
    public const int WouldBlock = 11;
    public const int PermissionDenied = 13;
    public const int Busy = 16;
    public const int NoSuchDevice = 19;
    public const int Overflowed = 75;
}

public static class DvbFailure
{
    public static DvbDeviceException Refused(string what) =>
        new(what, TuningFailure.Unspecified);

    public static DvbDeviceException NoLock(string what) => new(what, TuningFailure.NoLock);

    public static DvbDeviceException LockedWithoutData(string what) =>
        new(what, TuningFailure.LockedWithoutData);

    public static DvbDeviceException AtDevice(
        string devicePath,
        string operation,
        int error,
        string consequence
    ) =>
        new(
            $"{devicePath}: {operation} failed — {Describe(error)}. {consequence}",
            TuningFailure.DeviceUnusable,
            error
        );

    public static string Describe(int error) =>
        $"errno {error} ({Marshal.GetPInvokeErrorMessage(error)})";
}
