using Carina.Api.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Api.Tests.Unit;

public sealed class OutgoingRequestLinesTests
{
    private const string ClientCategory = "System.Net.Http.HttpClient.Default.LogicalHandler";

    [Theory(DisplayName = "BR-NA-003: the lines of an outgoing request below a warning are kept out, whatever level is configured")]
    [InlineData(LogLevel.Trace, false)]
    [InlineData(LogLevel.Debug, false)]
    [InlineData(LogLevel.Information, false)]
    [InlineData(LogLevel.Warning, true)]
    [InlineData(LogLevel.Error, true)]
    public void TheLinesOfAnOutgoingRequestBelowAWarningAreKeptOut(LogLevel level, bool written)
    {
        using ServiceProvider provider = TurnedUpToItsFinest();

        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(ClientCategory);

        Assert.Equal(written, logger.IsEnabled(level));
    }

    [Fact(DisplayName = "BR-NA-003: keeping the lines of an outgoing request out leaves every other category as configured")]
    public void KeepingTheLinesOfAnOutgoingRequestOutLeavesEveryOtherCategoryAsConfigured()
    {
        using ServiceProvider provider = TurnedUpToItsFinest();

        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Carina.Infrastructure.Notifications.Anything");

        Assert.True(logger.IsEnabled(LogLevel.Trace));
    }

    private static ServiceProvider TurnedUpToItsFinest()
        => new ServiceCollection()
            .AddLogging(logging => logging
                .AddProvider(new Hearing())
                .SetMinimumLevel(LogLevel.Trace)
                .AddFilter("System.Net.Http", LogLevel.Trace)
                .AddFilter<Hearing>("System.Net.Http.HttpClient", LogLevel.Trace)
                .KeepRequestLinesOut())
            .BuildServiceProvider();

    private sealed class Hearing : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Everything();

        public void Dispose()
        {
        }

        private sealed class Everything : ILogger
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
            }
        }
    }
}
