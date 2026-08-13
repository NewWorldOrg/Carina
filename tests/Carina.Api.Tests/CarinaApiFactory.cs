using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Carina.Api.Tests;

public sealed class CarinaApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "ConnectionStrings:Carina",
            "Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder");
    }
}
