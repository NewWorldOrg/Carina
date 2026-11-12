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

internal sealed class RecordedStartup : ILoggerProvider
{
    private readonly List<SaidLine> said = [];

    public IReadOnlyList<SaidLine> Everything
    {
        get
        {
            lock (said)
            {
                return [.. said];
            }
        }
    }

    public IReadOnlyList<SaidLine> By<T>()
    {
        lock (said)
        {
            return [.. said.Where(line => string.Equals(line.Category, typeof(T).FullName, StringComparison.Ordinal))];
        }
    }

    public IReadOnlyList<SaidLine> By<T>(LogLevel level)
    {
        lock (said)
        {
            return [.. said.Where(line =>
                string.Equals(line.Category, typeof(T).FullName, StringComparison.Ordinal)
                && line.Level == level)];
        }
    }

    public ILogger CreateLogger(string categoryName) => new Line(said, categoryName);

    public void Dispose()
    {
    }

    private sealed class Line(List<SaidLine> said, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            var line = new SaidLine(
                category,
                logLevel,
                formatter(state, exception),
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? [],
                exception?.ToString() ?? string.Empty);

            lock (said)
            {
                said.Add(line);
            }
        }
    }
}
