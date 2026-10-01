using Microsoft.Extensions.Logging.Console;

namespace Carina.Api.Extensions;

public static class LoggingBuilderExtensions
{
    public const string LineTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z' ";

    /// <summary>
    /// Has every line written to the console begin with the moment it was written, in UTC.
    /// </summary>
    public static ILoggingBuilder AddConsoleLinesWithTheirTime(this ILoggingBuilder logging)
    {
        ArgumentNullException.ThrowIfNull(logging);

        return logging.AddSimpleConsole(line =>
        {
            line.TimestampFormat = LineTimeFormat;
            line.UseUtcTimestamp = true;
        });
    }
}
