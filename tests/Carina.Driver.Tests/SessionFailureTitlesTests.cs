using Carina.Contracts;
using Carina.Driver.Recording;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class SessionFailureTitlesTests
{
    private const int NoSpaceLeftOnDevice = 28;

    private const int InputOutputError = 5;

    [Fact]
    public void AWriteTheDiskHadNoRoomForIsNamedAsAFullDisk()
        => Assert.Equal(SessionRefusalTitles.DiskFull, SessionFailureTitles.Of(WrittenToAFullDevice()));

    [Fact]
    public void AFullDiskIsFoundInsideTheFailureThatCarriesIt()
    {
        var full = new IOException("No space left on device : 'k-1.ts'", NoSpaceLeftOnDevice);

        Assert.Equal(SessionRefusalTitles.DiskFull, SessionFailureTitles.Of(new RecordingWriteException(full)));
        Assert.Equal(
            SessionRefusalTitles.DiskFull,
            SessionFailureTitles.Of(new AggregateException(new IOException("the device went away"), full)));
    }

    [Fact]
    public void AWriteThatFailedForAnotherReasonIsNotBlamedOnTheDisk()
    {
        Assert.Null(SessionFailureTitles.Of(new IOException("Input/output error", InputOutputError)));
        Assert.Null(SessionFailureTitles.Of(new IOException("No space left on device")));
        Assert.Null(SessionFailureTitles.Of(new UnauthorizedAccessException("Access to the path is denied.")));
        Assert.Null(SessionFailureTitles.Of(null));
    }

    [Fact]
    public void AReceptionFailureKeepsTheWordItAlreadyHad()
        => Assert.Equal(
            SessionRefusalTitles.NoData,
            SessionFailureTitles.Of(DvbFailure.LockedWithoutData("the demux delivered nothing")));

    private static IOException WrittenToAFullDevice()
    {
        using var device = new FileStream("/dev/full", FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 0);

        return Assert.ThrowsAny<IOException>(() => device.Write(new byte[64]));
    }
}
