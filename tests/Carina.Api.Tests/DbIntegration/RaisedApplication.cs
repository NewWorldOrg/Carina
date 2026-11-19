using Carina.Api.Tests.Support;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Carina.Api.Tests.DbIntegration;

public sealed class RaisedApplication(TestingWebApplicationFactory root, WebApplicationFactory<Program> built) : IAsyncDisposable
{
    public IServiceProvider Services => built.Services;

    public async ValueTask DisposeAsync()
    {
        await built.DisposeAsync();
        await root.DisposeAsync();
    }
}
