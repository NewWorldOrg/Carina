using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class LinuxDvbSystemCallsTests
{
    private const string AlwaysReadable = "/dev/zero";
    private const string AlwaysThere = "/dev/null";

    private const int NoSuchFileOrDirectory = 2;
    private const int NotADeviceThatTakesThisCall = 25;

    private readonly LinuxDvbSystemCalls calls = new();

    private static void MakePipe(string path)
    {
        using var making = System.Diagnostics.Process.Start("mkfifo", [path]);

        Assert.NotNull(making);
        making.WaitForExit();

        if (making.ExitCode is not 0 || !File.Exists(path))
        {
            throw new InvalidOperationException(
                $"This test needs a named pipe at '{path}' to prove the streaming access mode does not block, and mkfifo did not make one."
            );
        }
    }

    public LinuxDvbSystemCallsTests()
    {
        if (!File.Exists(AlwaysThere) || !File.Exists(AlwaysReadable))
        {
            throw new InvalidOperationException(
                $"These tests exercise the real syscalls against {AlwaysThere} and {AlwaysReadable}, which every Linux machine has. This one does not, so the syscall layer went unverified rather than untested."
            );
        }
    }

    [Fact]
    public void OpeningANodeThatIsNotThereComesBackWithTheKernelsOwnErrno()
    {
        SyscallOutcome opened = calls.Open("/dev/dvb/adapter99/frontend99", DvbAccess.Inspect);

        Assert.True(opened.Refused);
        Assert.Equal(NoSuchFileOrDirectory, opened.Error);
    }

    [Fact]
    public void ANodeThatIsThereOpensAndGivesItsDescriptorBack()
    {
        SyscallOutcome opened = calls.Open(AlwaysThere, DvbAccess.Control);

        Assert.False(opened.Refused);
        Assert.True(opened.Value > 2);
        Assert.False(calls.Close(opened.Value).Refused);
    }

    [Fact]
    public void AnAccessThatSaysNothingIsRefusedRatherThanGuessedAt()
    {
        Assert.Throws<DvbDeviceException>(
            () => calls.Open(AlwaysThere, DvbAccess.Unspecified)
        );
    }

    [Fact]
    public void ThePropertySettingCallLinksAndBringsBackTheErrnoTheKernelSet()
    {
        SyscallOutcome opened = calls.Open(AlwaysThere, DvbAccess.Control);

        try
        {
            DvbPropertyList properties = DvbTuning.PropertiesFor(DvbChannel.Terrestrial(55));
            SyscallOutcome set = calls.SetProperties(opened.Value, properties.Bytes);

            Assert.True(set.Refused);
            Assert.Equal(NotADeviceThatTakesThisCall, set.Error);
        }
        finally
        {
            calls.Close(opened.Value);
        }
    }

    [Fact]
    public void TheStatusReadCallLinksAndBringsBackTheErrnoTheKernelSet()
    {
        SyscallOutcome opened = calls.Open(AlwaysThere, DvbAccess.Control);

        try
        {
            SyscallOutcome read = calls.ReadStatus(opened.Value, out uint flags);

            Assert.True(read.Refused);
            Assert.Equal(NotADeviceThatTakesThisCall, read.Error);
            Assert.Equal(0u, flags);
        }
        finally
        {
            calls.Close(opened.Value);
        }
    }

    [Fact]
    public void TheVoltageCallLinksAndBringsBackTheErrnoTheKernelSet()
    {
        SyscallOutcome opened = calls.Open(AlwaysThere, DvbAccess.Control);

        try
        {
            SyscallOutcome set = calls.SetLnbVoltage(opened.Value, LnbVoltage.Off);

            Assert.True(set.Refused);
            Assert.Equal(NotADeviceThatTakesThisCall, set.Error);
        }
        finally
        {
            calls.Close(opened.Value);
        }
    }

    [Fact]
    public void TheFilterCallLinksAndBringsBackTheErrnoTheKernelSet()
    {
        SyscallOutcome opened = calls.Open(AlwaysThere, DvbAccess.Control);

        try
        {
            SyscallOutcome set = calls.SetPesFilter(
                opened.Value,
                DemuxFilter.EverythingFromTheFrontend()
            );

            Assert.True(set.Refused);
            Assert.Equal(NotADeviceThatTakesThisCall, set.Error);
        }
        finally
        {
            calls.Close(opened.Value);
        }
    }

    [Fact]
    public void ReadingActuallyReadsThroughToTheKernel()
    {
        SyscallOutcome opened = calls.Open(AlwaysReadable, DvbAccess.Stream);

        try
        {
            byte[] buffer = new byte[188];
            SyscallOutcome read = calls.ReadBytes(opened.Value, buffer, buffer.Length);

            Assert.False(read.Refused);
            Assert.Equal(188, read.Value);
        }
        finally
        {
            calls.Close(opened.Value);
        }
    }

    [Fact]
    public void WaitingOnSomethingAlwaysReadableComesBackReadable()
    {
        SyscallOutcome opened = calls.Open(AlwaysReadable, DvbAccess.Stream);

        try
        {
            SyscallOutcome ready = calls.WaitForReadable(opened.Value, 0);

            Assert.False(ready.Refused);
            Assert.Equal(1, ready.Value);
        }
        finally
        {
            calls.Close(opened.Value);
        }
    }

    [Fact]
    public void WaitingOnADescriptorThatWasNeverOpenedTimesOutRatherThanReportingData()
    {
        SyscallOutcome ready = calls.WaitForReadable(-1, 0);

        Assert.False(ready.Refused);
        Assert.Equal(0, ready.Value);
    }

    [Fact]
    public async Task StreamingAccessDoesNotBlockOnAPipeThatNobodyIsWritingTo()
    {
        string work = Directory.CreateTempSubdirectory("carina-fifo-").FullName;

        try
        {
            string pipe = Path.Combine(work, "dvr");
            MakePipe(pipe);

            Task<SyscallOutcome> opening = Task.Run(() => calls.Open(pipe, DvbAccess.Stream));
            Task settled = await Task.WhenAny(opening, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.True(
                settled == opening,
                "Opening a pipe with no writer never returned, so the streaming access mode is not passing the non-blocking flag."
            );

            SyscallOutcome opened = await opening;

            Assert.False(opened.Refused);
            calls.Close(opened.Value);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [Fact]
    public void RestingStopsEarlyWhenTheSessionIsAlreadyCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        long before = Environment.TickCount64;
        calls.Rest(TimeSpan.FromSeconds(30), cancellation.Token);

        Assert.True(Environment.TickCount64 - before < 5_000);
    }
}
