using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.Unit;

public sealed class StubEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "Carina.Api.Tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } =
        new NullFileProvider();
}
