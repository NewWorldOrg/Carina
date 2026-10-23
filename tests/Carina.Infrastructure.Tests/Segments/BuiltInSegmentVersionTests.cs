using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class BuiltInSegmentVersionTests : IDisposable
{
    private static readonly DateTime Started = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly HeldSegmentModelVersions versions = new();

    private readonly KeptLines<BuiltInSegmentVersion> log = new();

    private readonly ServiceProvider provider;

    public BuiltInSegmentVersionTests()
    {
        provider = new ServiceCollection()
            .AddSingleton<ISegmentModelVersionRepository>(versions)
            .BuildServiceProvider();
    }

    public void Dispose() => provider.Dispose();

    [Fact(DisplayName = "BR-CS-003: starting keeps the built-in version of this release once, as of the moment the application starts")]
    public async Task StartingKeepsTheBuiltInVersion()
    {
        await Kept().StartAsync(CancellationToken.None);

        Assert.Equal([(SegmentModel.BuiltIn, Started)], versions.BuiltInAsked);
        Assert.DoesNotContain(log.Lines, line => line.Level >= LogLevel.Warning);
    }

    [Fact(DisplayName = "BR-CS-003: a built-in version that cannot be kept is said so, and the application starts all the same")]
    public async Task ABuiltInVersionThatCannotBeKeptIsSaidSo()
    {
        versions.BuiltInFailure = new InvalidOperationException("the store is not there");

        await Kept().StartAsync(CancellationToken.None);

        (LogLevel level, string line, Exception? failure) = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains("next start", line, StringComparison.Ordinal);
        Assert.Same(versions.BuiltInFailure, failure);
    }

    private BuiltInSegmentVersion Kept()
        => new(provider.GetRequiredService<IServiceScopeFactory>(), new HandTurnedClock(new DateTimeOffset(Started)), log);
}
