extern alias driver;

using System.Runtime.Versioning;

using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using driver::Puppis.Driver.Configuration;
using driver::Puppis.Driver.Descrambling;
using driver::Puppis.Driver.Tuning;

using Puppis.Contracts;

namespace Carina.Api.Tests.Support;

[SupportedOSPlatform("linux")]
internal sealed class StreamThatEndsItselfOnce(long after) : ITunerDeviceFactory
{
    private static readonly TimeSpan BetweenReads = TimeSpan.FromMilliseconds(25);

    private int handedOut;

    public ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune, HeadHold hold)
    {
        TuningRequest asked = tune?.ToLegacyRequest() ?? tuning;
        var paced = new PacedTunerDevice(
            new FakeTunerDevice(asked.PhysicalChannel, asked.ServiceId),
            BetweenReads);

        return Interlocked.Increment(ref handedOut) is 1
            ? new EndsCleanlyAfter(paced, after)
            : paced;
    }
}
