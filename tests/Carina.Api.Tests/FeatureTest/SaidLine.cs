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

internal sealed record SaidLine(
    string Category,
    LogLevel Level,
    string Text,
    IReadOnlyList<KeyValuePair<string, object?>> Values,
    string Thrown)
{
    public string Only(string name)
        => Values.FirstOrDefault(value => value.Key == name).Value?.ToString() ?? string.Empty;
}
