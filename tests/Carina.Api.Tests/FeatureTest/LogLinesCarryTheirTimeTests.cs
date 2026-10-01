using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Carina.Api.Tests.FeatureTest;

public sealed class LogLinesCarryTheirTimeTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private static readonly DateTimeOffset Written = new(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero);

    [Fact(DisplayName = "a line the application writes to its console begins with the moment it was written, in UTC and to the millisecond")]
    public void ALineTheApplicationWritesToItsConsoleBeginsWithTheMomentItWasWritten()
    {
        SimpleConsoleFormatterOptions line = factory.Services
            .GetRequiredService<IOptionsMonitor<SimpleConsoleFormatterOptions>>()
            .CurrentValue;

        Assert.True(line.UseUtcTimestamp);
        Assert.Equal(
            "2026-01-02T03:04:05.678Z ",
            Written.ToString(line.TimestampFormat, CultureInfo.InvariantCulture));
    }

    [Fact(DisplayName = "the console writes its lines in the form that carries the moment")]
    public void TheConsoleWritesItsLinesInTheFormThatCarriesTheMoment()
    {
        ConsoleLoggerOptions console = factory.Services
            .GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>()
            .CurrentValue;

        Assert.Equal(ConsoleFormatterNames.Simple, console.FormatterName);
    }
}
