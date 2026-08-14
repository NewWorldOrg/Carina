using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class LinuxDvbSystemCallsTests
{
    private const string AlwaysReadable = "/dev/zero";
    private const string AlwaysThere = "/dev/null";

    private const int NoSuchFileOrDirectory = 2;
    private const int NotADeviceThatTakesThisCall = 25;

    private readonly LinuxDvbSystemCalls calls = new();

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
        var opened = calls.Open("/dev/dvb/adapter99/frontend99", DvbAccess.Inspect);

        Assert.True(opened.Refused);
        Assert.Equal(NoSuchFileOrDirectory, opened.Error);
    }

    [Fact]
    public void ANodeThatIsThereOpensAndGivesItsDescriptorBack()
    {
        var opened = calls.Open(AlwaysThere, DvbAccess.Control);

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
    public void ATuningCallToSomethingThatIsNotAFrontendComesBackRefused()
    {
        var opened = calls.Open(AlwaysThere, DvbAccess.Control);

        try
        {
            var properties = DvbTuning.PropertiesFor(DvbChannel.Terrestrial(27));
            var set = calls.SetProperties(opened.Value, properties.Bytes);

            Assert.True(set.Refused);
            Assert.Equal(NotADeviceThatTakesThisCall, set.Error);
        }
        finally
        {
            calls.Close(opened.Value);
        }
    }

    [Fact]
    public void AStatusReadFromSomethingThatIsNotAFrontendComesBackRefused()
    {
        var opened = calls.Open(AlwaysThere, DvbAccess.Control);

        try
        {
            var read = calls.ReadStatus(opened.Value, out var flags);

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
    public void AVoltageCallToSomethingThatIsNotAFrontendComesBackRefused()
    {
        var opened = calls.Open(AlwaysThere, DvbAccess.Control);

        try
        {
            var set = calls.SetLnbVoltage(opened.Value, LnbVoltage.Off);

            Assert.True(set.Refused);
            Assert.Equal(NotADeviceThatTakesThisCall, set.Error);
        }
        finally
        {
            calls.Close(opened.Value);
        }
    }

    [Fact]
    public void AFilterCallToSomethingThatIsNotADemuxComesBackRefused()
    {
        var opened = calls.Open(AlwaysThere, DvbAccess.Control);

        try
        {
            var set = calls.SetPesFilter(
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
        var opened = calls.Open(AlwaysReadable, DvbAccess.Stream);

        try
        {
            var buffer = new byte[188];
            var read = calls.ReadBytes(opened.Value, buffer, buffer.Length);

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
        var opened = calls.Open(AlwaysReadable, DvbAccess.Stream);

        try
        {
            var ready = calls.WaitForReadable(opened.Value, 0);

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
        var ready = calls.WaitForReadable(-1, 0);

        Assert.False(ready.Refused);
        Assert.Equal(0, ready.Value);
    }

    [Fact]
    public void RestingStopsEarlyWhenTheSessionIsAlreadyCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var before = Environment.TickCount64;
        calls.Rest(TimeSpan.FromSeconds(30), cancellation.Token);

        Assert.True(Environment.TickCount64 - before < 5_000);
    }
}
