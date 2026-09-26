using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Tests.Collection;

internal sealed class HeardLog : ILoggerProvider
{
    private readonly ConcurrentQueue<IReadOnlyDictionary<string, object?>> heard = new();

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Entries => [.. heard];

    public ILogger<T> For<T>() => new Logger<T>(new LoggerFactory([this]));

    public ILogger CreateLogger(string categoryName) => new Listener(heard);

    public void Dispose()
    {
    }

    private sealed class Listener(ConcurrentQueue<IReadOnlyDictionary<string, object?>> heard) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> fields)
            {
                heard.Enqueue(fields.ToDictionary(field => field.Key, field => field.Value));
            }
        }
    }
}
