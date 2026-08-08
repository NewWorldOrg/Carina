using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Carina.Api.Tests;

/// <summary>
/// Hosts the API in-process for feature tests. Configuration is supplied here so
/// that tests exercise the same startup path as the deployed process.
/// </summary>
public sealed class CarinaApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "ConnectionStrings:Carina",
            "Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder");
    }
}
