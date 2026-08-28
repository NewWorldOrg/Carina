using System.Globalization;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
public sealed class MigrationCultureTests
{
    [Fact]
    public async Task TheEntryPointDecidesItsOwnCultureRatherThanTakingOneFromTheEnvironment()
    {
        CultureInfo? held = CultureInfo.DefaultThreadCurrentCulture;
        CultureInfo? spoken = CultureInfo.DefaultThreadCurrentUICulture;

        try
        {
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("tr-TR");
            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("tr-TR");

            Assert.Equal(
                DbEntryPoint.UsageExitCode,
                await DbEntryPoint.RunAsync(["--not-a-thing-it-does"], new StringWriter()));
            Assert.Same(CultureInfo.InvariantCulture, CultureInfo.DefaultThreadCurrentCulture);
            Assert.Same(CultureInfo.InvariantCulture, CultureInfo.DefaultThreadCurrentUICulture);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = held;
            CultureInfo.DefaultThreadCurrentUICulture = spoken;
        }
    }
}
