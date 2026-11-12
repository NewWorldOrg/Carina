using System.Net;
using System.Net.Http.Json;

using Carina.Api.Authentication;
using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class RefusingLocalAccounts : ILocalAccountRepository
{
    public Task<LocalAccount?> FindAsync(CancellationToken cancellationToken)
        => Task.FromException<LocalAccount?>(new InvalidOperationException("The store is out of reach."));

    public Task SaveAsync(LocalAccount account, CancellationToken cancellationToken)
        => Task.FromException(new InvalidOperationException("The store is out of reach."));
}
