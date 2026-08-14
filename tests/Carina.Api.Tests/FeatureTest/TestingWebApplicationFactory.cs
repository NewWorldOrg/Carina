using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

public class TestingWebApplicationFactory : WebApplicationFactory<Program>
{
    public string DriverSocketPath { get; init; } =
        Path.Combine(Path.GetTempPath(), "carina-feature-tests", "no-driver.sock");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(
            "ConnectionStrings:Carina",
            "Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder");
        builder.UseSetting("CARINA_DRIVER_SOCKET", DriverSocketPath);
    }
}
