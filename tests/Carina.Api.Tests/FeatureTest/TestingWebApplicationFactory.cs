using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Carina.Api.Tests.FeatureTest;

public class TestingWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "ConnectionStrings:Carina",
            "Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder");
        builder.UseSetting("CARINA_DRIVER_SOCKET", "/run/carina/driver.sock");
    }
}
