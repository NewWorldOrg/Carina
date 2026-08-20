using Carina.Api.Authentication;

using Microsoft.Extensions.Logging;

namespace Carina.Api.Tests.Unit;

public sealed class AnonymousNetworkDiagnosisTests
{
    private readonly RecordingLogger logger = new();

    [Fact]
    public async Task AnInstallationNamingNoNetworkIsToldNothingBecauseNamingNoneIsTheSafeState()
    {
        await Diagnosing(AnonymousNetworks.Named(null)).StartAsync(CancellationToken.None);

        Assert.Empty(logger.Lines);
    }

    [Fact]
    public async Task TheNetworksNamedAreReadBackAtStartupSoTheyAreNotForgotten()
    {
        await Diagnosing(AnonymousNetworks.Named("10.0.0.0/8, fd00::/8")).StartAsync(CancellationToken.None);

        (LogLevel Level, string Said) said = Assert.Single(logger.Lines);

        Assert.Equal(LogLevel.Warning, said.Level);
        Assert.Contains(AnonymousNetworks.Key, said.Said, StringComparison.Ordinal);
        Assert.Contains("10.0.0.0/8", said.Said, StringComparison.Ordinal);
        Assert.Contains("fd00::/8", said.Said, StringComparison.Ordinal);
    }

    private AnonymousNetworkDiagnosis Diagnosing(AnonymousNetworks named) => new(named, logger);

    private sealed class RecordingLogger : ILogger<AnonymousNetworkDiagnosis>
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
