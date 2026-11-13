using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

public sealed class VaapiRateControlFixtures
{
    public BitrateCap VaapiRateControl => new(3000);

    public int VaapiKilobitsPerSecond => 3000;
}
