using System.Runtime.InteropServices;

namespace Carina.Driver.Tuning.Dvb;

public sealed class DvbDeviceException(string message) : Exception(message);

public static class DvbFailure
{
    public static DvbDeviceException Refused(string what) => new(what);

    public static DvbDeviceException AtDevice(
        string devicePath,
        string operation,
        int error,
        string consequence
    ) =>
        new(
            $"{devicePath}: {operation} failed with errno {error} ({Marshal.GetPInvokeErrorMessage(error)}). {consequence}"
        );
}
