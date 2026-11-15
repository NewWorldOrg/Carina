extern alias driver;

using System.Runtime.Versioning;

using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using driver::Puppis.Driver.Configuration;
using driver::Puppis.Driver.Descrambling;
using driver::Puppis.Driver.Tuning;

using Puppis.Contracts;

namespace Carina.Api.Tests.Support;

/// <summary>
/// A device that hands over some bytes and then returns none at all: a stream ending cleanly.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class EndsCleanlyAfter(ITunerDevice carrying, long after) : ITunerDevice
{
    private long given;

    public long Overflows => carrying.Overflows;

    public ISignalQualitySource? Quality => carrying.Quality;

    public byte[] Read(int count, CancellationToken cancellationToken)
    {
        if (Interlocked.Read(ref given) >= after)
        {
            return [];
        }

        byte[] chunk = carrying.Read(count, cancellationToken);

        Interlocked.Add(ref given, chunk.Length);

        return chunk;
    }

    public void Dispose() => carrying.Dispose();
}
