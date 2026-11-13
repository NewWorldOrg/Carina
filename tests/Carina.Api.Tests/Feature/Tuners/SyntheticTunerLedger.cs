extern alias driver;

using System.Runtime.Versioning;

using Carina.Api.Tests.FeatureTest;

using driver::Puppis.Driver.Configuration;

namespace Carina.Api.Tests.Feature.Tuners;

[SupportedOSPlatform("linux")]
internal static class SyntheticTunerLedger
{
    public const string Ground = "synthetic-terrestrial";

    public const string Sky = "synthetic-satellite";

    public static IReadOnlyList<DeviceSettings> Written(SyntheticDriverHost driver)
    {
        DriverConfiguration? written = DriverConfigurationReader.Parse(File.ReadAllText(driver.LedgerPath));

        Assert.NotNull(written);

        return written.Devices ?? [];
    }
}
