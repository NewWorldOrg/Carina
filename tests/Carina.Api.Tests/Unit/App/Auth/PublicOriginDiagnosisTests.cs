using Carina.Api.Authentication;

using Microsoft.Extensions.Logging;

namespace Carina.Api.Tests.Unit;

public sealed class PublicOriginDiagnosisTests
{
    private readonly RecordingLogger logger = new();

    [Fact]
    public async Task AnInstallationNamingNoOriginIsWarnedAtStartupRatherThanAtTheProvider()
    {
        await Diagnosing(PublicOrigin.Named(null)).StartAsync(CancellationToken.None);

        (LogLevel Level, string Said) said = Assert.Single(logger.Lines);

        Assert.Equal(LogLevel.Warning, said.Level);
        Assert.Contains(PublicOrigin.Key, said.Said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInstallationThatNamesItsPublicOriginSaysWhichRedirectUriItWillUse()
    {
        await Diagnosing(PublicOrigin.Named("https://carina.example")).StartAsync(CancellationToken.None);

        (LogLevel Level, string Said) said = Assert.Single(logger.Lines);

        Assert.Equal(LogLevel.Information, said.Level);
        Assert.Contains(
            $"https://carina.example{OidcHandshake.CallbackPath}",
            said.Said,
            StringComparison.Ordinal);
    }

    private PublicOriginDiagnosis Diagnosing(PublicOrigin origin) => new(origin, logger);

    private sealed class RecordingLogger : ILogger<PublicOriginDiagnosis>
    {
        public List<(LogLevel Level, string Said)> Lines { get; } = [];

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

            Lines.Add((logLevel, formatter(state, exception)));
        }
    }
}
