using Carina.Api.Authentication;

using Microsoft.Extensions.Logging;

namespace Carina.Api.Tests.Unit;

public sealed class TrustedProxyDiagnosisTests
{
    private readonly RecordingLogger logger = new();

    [Fact]
    public async Task AnInstallationTrustingNothingIsWarnedAtStartupRatherThanAtTheFirstFailedSignIn()
    {
        await Diagnosing(TrustedProxies.Named(null, null)).StartAsync(CancellationToken.None);

        (LogLevel Level, string Said) said = Assert.Single(logger.Lines);

        Assert.Equal(LogLevel.Warning, said.Level);
        Assert.Contains(TrustedProxies.ProxiesKey, said.Said, StringComparison.Ordinal);
        Assert.Contains(TrustedProxies.NetworksKey, said.Said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInstallationThatNamesItsProxySaysWhatItTrusts()
    {
        await Diagnosing(TrustedProxies.Named("10.0.0.1", "172.16.0.0/12"))
            .StartAsync(CancellationToken.None);

        (LogLevel Level, string Said) said = Assert.Single(logger.Lines);

        Assert.Equal(LogLevel.Information, said.Level);
        Assert.Contains("10.0.0.1", said.Said, StringComparison.Ordinal);
        Assert.Contains("172.16.0.0/12", said.Said, StringComparison.Ordinal);
    }

    private TrustedProxyDiagnosis Diagnosing(TrustedProxies trusted) => new(trusted, logger);

    private sealed class RecordingLogger : ILogger<TrustedProxyDiagnosis>
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
