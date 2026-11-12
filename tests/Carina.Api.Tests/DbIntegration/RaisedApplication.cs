using Carina.Api.Tests.FeatureTest;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Carina.Api.Tests.DbIntegration;

/// <summary>
/// The application raised over the scratch database, and the host it was raised from, let go of together.
/// </summary>
public sealed class RaisedApplication(TestingWebApplicationFactory root, WebApplicationFactory<Program> built) : IAsyncDisposable
{
    public IServiceProvider Services => built.Services;

    public async ValueTask DisposeAsync()
    {
        await built.DisposeAsync();
        await root.DisposeAsync();
    }
}
