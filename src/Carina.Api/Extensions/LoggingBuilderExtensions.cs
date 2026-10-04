using Microsoft.Extensions.Logging.Console;

namespace Carina.Api.Extensions;

public static class LoggingBuilderExtensions
{
    public const string LineTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z' ";

    public static readonly IReadOnlyList<string> CategoriesThatNameTheRequestPath =
    [
        "Microsoft.AspNetCore.Hosting.Diagnostics",
        "Microsoft.AspNetCore.Routing.Matching",
    ];

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

    /// <summary>
    /// Keeps the framework from writing the lines that name the path a request came on, whatever level is configured.
    /// </summary>
    public static ILoggingBuilder KeepRequestLinesOut(this ILoggingBuilder logging)
    {
        ArgumentNullException.ThrowIfNull(logging);

        foreach (string category in CategoriesThatNameTheRequestPath)
        {
            logging.AddFilter(category, LogLevel.Warning);
        }

        return logging;
    }
}
