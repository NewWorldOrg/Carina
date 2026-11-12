using Carina.Infrastructure.Auth;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Tests.Auth;

internal sealed class RecordedLog : ILogger<LocalAccountBootstrap>
{
    public List<string> Lines { get; } = [];

    public List<IReadOnlyList<KeyValuePair<string, object?>>> Values { get; } = [];

    public List<string> Failures { get; } = [];

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

        Lines.Add(formatter(state, exception));

        if (exception is not null)
        {
            Failures.Add(exception.Message);
        }

        if (state is IReadOnlyList<KeyValuePair<string, object?>> named)
        {
            Values.Add(named);
        }
    }

    public string Only(string name)
    {
        KeyValuePair<string, object?>[] found = [.. Values.SelectMany(values => values).Where(value => value.Key == name)];

        return Assert.Single(found).Value?.ToString() ?? string.Empty;
    }
}
